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
	LoadAccount account)
	: IAsyncDisposable
{
	private readonly HttpClient _http = clientFactory.CreateClient();
	private readonly List<byte[]> _outbox = [];
	private string? _token;

	/// <summary>Gets a value indicating whether this client currently holds a live session token.</summary>
	public bool IsLoggedIn => _token is not null;

	public async ValueTask DisposeAsync()
	{
		await LogoutAsync();
		_http.Dispose();
	}

	/// <summary>
	///     Logs in. Success is decided from the decoded <c>UserId</c> login-reply packet, never from the
	///     HTTP status code — a failed login still returns <c>200 OK</c> with a failure reply and no
	///     usable token.
	/// </summary>
	public async Task<LoginOutcome> LoginAsync(CancellationToken cancellationToken = default)
	{
		var body = LoginFormBuilder.Build(account);
		using var content = new ByteArrayContent(body);
		using var request = new HttpRequestMessage(HttpMethod.Post, clientFactory.BuildUri("c", "/"));
		request.Content = content;

		// The scenario token must not interrupt the login I/O: a warm-up or teardown that cancels a
		// login mid-flight would drop the response (and its `cho-token`) after the server has already
		// created the session, leaving no token to close it with. The client's own timeout still
		// bounds the request, and the caller observes the token before and after the call.
		cancellationToken.ThrowIfCancellationRequested();
		using var response = await _http.SendAsync(request, CancellationToken.None);
		var choToken = response.Headers.TryGetValues("cho-token", out var values)
			? values.FirstOrDefault()
			: null;

		// A real token is always `osu-{guid}`; every failure carries an error string instead.
		// Record it as soon as the response arrives — so a caller whose iteration is canceled
		// before the body is read can still close the session it just opened.
		if (choToken is not null && choToken.StartsWith("osu-", StringComparison.Ordinal))
			_token = choToken;

		var responseBytes = await response.Content.ReadAsByteArrayAsync(CancellationToken.None);
		var frames = ServerPacketStream.ReadFrames(responseBytes);
		var reply = frames.FirstOrDefault(f => f.Type == ServerPackets.UserId);

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
		if (!IsLoggedIn) throw new InvalidOperationException("PollAsync called before a successful login.");

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
	///     Sends a <c>Logout</c> packet and forgets the local token. The server holds no login
	///     grace, so a logout always closes the session; it is retried up to three times with a
	///     short backoff if the server does not acknowledge it.
	/// </summary>
	public async Task LogoutAsync(CancellationToken cancellationToken = default)
	{
		if (!IsLoggedIn) return;

		for (var attempt = 0; attempt < 3; attempt++)
			try
			{
				Send(ClientPacketWriter.Logout());
				await PollAsync(CancellationToken.None);
				_token = null;
				return;
			}
			catch when (attempt < 2)
			{
				await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
			}
	}
}