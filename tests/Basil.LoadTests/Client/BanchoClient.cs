using System.Buffers.Binary;
using Basil.LoadTests.Models;
using Basil.Protocol.Packets;

namespace Basil.LoadTests.Client;

/// <summary>
///     One virtual user's bancho connection: login, poll, queue-and-send, logout. Owns the
///     <c>osu-token</c> for its account and never lets two <see cref="BanchoClient" />s share one —
///     the server rejects a second concurrent session for the same username within 10 seconds of the
///     first, so each virtual user claims exactly one account for the run.
/// </summary>
public sealed class BanchoClient(
	BasilHttpClientFactory clientFactory,
	LoadAccount account,
	TimeSpan? minSessionHold = null)
	: IDisposable
{
	/// <summary>
	///     The default minimum time a session must be held before <see cref="LogoutAsync" /> sends the
	///     logout. The server silently ignores a logout sent within one second of login, so a client
	///     that cycles login→logout faster than this never actually closes its session.
	/// </summary>
	private static readonly TimeSpan DefaultMinSessionHold = TimeSpan.FromSeconds(1.5);

	private readonly HttpClient _http = clientFactory.CreateClient();
	private readonly TimeSpan _minSessionHold = minSessionHold ?? DefaultMinSessionHold;
	private readonly List<byte[]> _outbox = [];
	private string? _token;
	private DateTimeOffset _loginSentAt;
	private static int _sDebugCounter;

	/// <summary>Gets a value indicating whether this client currently holds a live session token.</summary>
	public bool IsLoggedIn => _token is not null;

	/// <summary>
	///     Logs in. Success is decided from the decoded <c>UserId</c> login-reply packet, never from the
	///     HTTP status code — a failed login still returns <c>200 OK</c> with a failure reply and no
	///     usable token.
	/// </summary>
	public async Task<LoginOutcome> LoginAsync(CancellationToken cancellationToken = default)
	{
		_loginSentAt = DateTimeOffset.UtcNow;
		var body = LoginFormBuilder.Build(account);
		using var content = new ByteArrayContent(body);
		using var request = new HttpRequestMessage(HttpMethod.Post, clientFactory.BuildUri("c", "/"))
		{
			Content = content
		};

		using var response = await _http.SendAsync(request, cancellationToken);
		var responseBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
		var choToken = response.Headers.TryGetValues("cho-token", out var values) ? values.FirstOrDefault() : null;

		var frames = ServerPacketStream.ReadFrames(responseBytes);
		var reply = frames.FirstOrDefault(f => f.Type == ServerPackets.UserId);

		var dbg = System.Threading.Interlocked.Increment(ref _sDebugCounter);
		if (dbg <= 400)
			Console.WriteLine($"[CLIENTDBG] #{dbg} acct={account.Name} choToken={(choToken ?? "<null>")} bytes={responseBytes.Length} frames={frames.Count} uidLen={reply.Payload?.Length}");

		if (reply.Payload is { Length: >= 4 } payload && choToken is not null)
		{
			var userId = BinaryPrimitives.ReadInt32LittleEndian(payload);
			if (userId > 0)
			{
				_token = choToken;
				account.UserId = userId;
				return LoginOutcome.Ok(choToken, userId);
			}
		}

		return LoginOutcome.Fail(choToken ?? "no-cho-token-header");
	}

	/// <summary>Queues a client packet to be sent on the next <see cref="PollAsync" /> call.</summary>
	public void Send(byte[] packet)
	{
		_outbox.Add(packet);
	}

	/// <summary>
	///     Sends any queued packets (or an empty body, which still counts as a keep-alive) and returns
	///     whatever the server had queued for this session.
	/// </summary>
	/// <exception cref="InvalidOperationException">Called before a successful <see cref="LoginAsync" />.</exception>
	public async Task<IReadOnlyList<ServerPacketFrame>> PollAsync(CancellationToken cancellationToken = default)
	{
		if (_token is null) throw new InvalidOperationException("PollAsync called before a successful login.");

		var body = _outbox.Count == 0 ? [] : _outbox.SelectMany(p => p).ToArray();
		_outbox.Clear();

		using var content = new ByteArrayContent(body);
		using var request = new HttpRequestMessage(HttpMethod.Post, clientFactory.BuildUri("c", "/"));
		request.Content = content;
		request.Headers.Add("osu-token", _token);

		using var response = await _http.SendAsync(request, cancellationToken);
		var responseBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
		return ServerPacketStream.ReadFrames(responseBytes);
	}

	/// <summary>
	///     Sends a <c>Logout</c> packet and forgets the local token. The logout is only sent once the
	///     session is older than the configured minimum hold — the server ignores a logout within one
	///     second of login, so a logout sent before that would leave the session alive and the next
	///     login for the account rejected. The hold is measured from when the login request was sent,
	///     the earliest anchor the client knows, so the server-side <c>LoginTime</c> always falls
	///     inside it.
	/// </summary>
	public async Task LogoutAsync(CancellationToken cancellationToken = default)
	{
		if (_token is null) return;

		var remaining = _minSessionHold - (DateTimeOffset.UtcNow - _loginSentAt);
		if (remaining > TimeSpan.Zero)
			await Task.Delay(remaining, cancellationToken);

		await SendLogoutAsync();
	}

	/// <summary>
	///     Best-effort cleanup that always tries to close the session, even when the caller is being
	///     cancelled (warm-up ending, shutdown, aborted run). Waits out the minimum hold so the server
	///     honors the logout, then sends it ignoring cancellation. A transport failure here is
	///     swallowed — the session may be leaked, but the caller's own outcome must not be masked by
	///     an exception from cleanup.
	/// </summary>
	public async Task LogoutIgnoringCancellationAsync()
	{
		if (_token is null) return;

		try
		{
			var remaining = _minSessionHold - (DateTimeOffset.UtcNow - _loginSentAt);
			if (remaining > TimeSpan.Zero)
				await Task.Delay(remaining);

			await SendLogoutAsync();
		}
		catch
		{
			// Best-effort by contract: a failed cleanup must not mask the iteration's real outcome.
		}
	}

	private async Task SendLogoutAsync()
	{
		Send(ClientPacketWriter.Logout());
		await PollAsync(CancellationToken.None);
		_token = null;
	}

	public void Dispose()
	{
		_http.Dispose();
	}
}
