using System.Diagnostics;
using System.Net;
using System.Text;

namespace Basil.Tests.Shared.Clients.Stable;

/// <summary>
///     An osu! client on the <c>c.</c> host: signs in, then sends the packets it queued and reads what the server
///     sends back, one poll at a time.
/// </summary>
/// <remarks>
///     A client is driven by one task; polls of one client must not overlap.
///     A refusal is an answer (the status and the packets read), and only a transport failure throws.
///     Packets queued for a poll that fails to reach the server are lost.
/// </remarks>
public sealed class StableClient(Endpoint endpoint, HttpClient http, StableIdentity identity)
{
	private readonly List<byte[]> _queue = [];

	/// <summary>Gets the identity this client signs in with.</summary>
	public StableIdentity Identity => identity;

	/// <summary>Gets the session token, or <see langword="null" /> while the client is not signed in.</summary>
	public string? Token { get; private set; }

	/// <summary>Gets the id of the signed-in user, or 0 while the client is not signed in.</summary>
	public int UserId { get; private set; }

	/// <summary>Signs in.</summary>
	/// <returns>The answer; <see cref="StableLoginResult.Accepted" /> tells whether a session started.</returns>
	public async Task<StableLoginResult> LoginAsync(CancellationToken ct)
	{
		using var request = NewRequest(null);
		request.Content = new StringContent(identity.LoginBody(), new UTF8Encoding(false));
		using var response = await http.SendAsync(request, ct);
		var body = await response.Content.ReadAsByteArrayAsync(ct);
		var packets = response.IsSuccessStatusCode ? StablePacket.ReadAll(body) : [];
		var token = response.Headers.TryGetValues("cho-token", out var values) ? values.FirstOrDefault() : null;
		var userId = packets.OfType<LoginReply>().FirstOrDefault()?.UserId ?? 0;
		var accepted = response.IsSuccessStatusCode && !string.IsNullOrEmpty(token) && token != "no" && userId > 0;
		if (accepted)
		{
			Token = token;
			UserId = userId;
		}

		return new StableLoginResult(response.StatusCode, accepted, userId, packets);
	}

	/// <summary>Queues packets for the next poll.</summary>
	public void Enqueue(params IEnumerable<byte[]> packets)
	{
		lock (_queue)
		{
			_queue.AddRange(packets);
		}
	}

	/// <summary>Sends the queued packets, or a keep-alive when none are queued, and reads the packets of the answer.</summary>
	public async Task<StableExchange> PollAsync(CancellationToken ct)
	{
		return await ExchangeAsync(TakeQueue(), ct);
	}

	/// <summary>Queues packets and polls at once.</summary>
	public Task<StableExchange> SendAsync(CancellationToken ct, params IEnumerable<byte[]> packets)
	{
		Enqueue(packets);
		return PollAsync(ct);
	}

	/// <summary>Polls until a packet of the wanted kind arrives, the wait runs out, or the server refuses a poll.</summary>
	/// <typeparam name="T">The kind of packet to wait for.</typeparam>
	/// <param name="match">An extra test the packet must pass, or <see langword="null" /> for any packet of the kind.</param>
	/// <param name="interval">The pause between polls.</param>
	/// <param name="timeout">How long to wait in all.</param>
	/// <param name="ct">Stops the wait.</param>
	/// <returns>
	///     The packet that matched, or <see langword="null" /> in <see cref="StableWait{T}.Match" />, with every packet
	///     read while waiting.
	/// </returns>
	public async Task<StableWait<T>> PollUntilAsync<T>(Func<T, bool>? match, TimeSpan interval, TimeSpan timeout,
		CancellationToken ct) where T : StablePacket
	{
		var started = Stopwatch.GetTimestamp();
		var all = new List<StablePacket>();
		while (true)
		{
			var exchange = await PollAsync(ct);
			all.AddRange(exchange.Packets);
			var found = exchange.Packets.OfType<T>().FirstOrDefault(packet => match is null || match(packet));
			if (found is not null || !exchange.IsSuccess || Stopwatch.GetElapsedTime(started) >= timeout)
				return new StableWait<T>(exchange.Status, found, all);

			await Task.Delay(interval, ct);
		}
	}

	/// <summary>Signs out and forgets the session.</summary>
	public async Task<StableExchange> LogoutAsync(CancellationToken ct)
	{
		Enqueue(ClientPackets.Logout());
		var exchange = await PollAsync(ct);
		Token = null;
		UserId = 0;
		return exchange;
	}

	private byte[] TakeQueue()
	{
		lock (_queue)
		{
			if (_queue.Count == 0) return ClientPackets.Ping();

			var body = _queue.SelectMany(packet => packet).ToArray();
			_queue.Clear();
			return body;
		}
	}

	private async Task<StableExchange> ExchangeAsync(byte[] body, CancellationToken ct)
	{
		if (Token is null) throw new InvalidOperationException("The client is not signed in.");

		using var request = NewRequest(Token);
		request.Content = new ByteArrayContent(body);
		using var response = await http.SendAsync(request, ct);
		var answer = await response.Content.ReadAsByteArrayAsync(ct);
		return new StableExchange(response.StatusCode,
			response.IsSuccessStatusCode ? StablePacket.ReadAll(answer) : []);
	}

	private HttpRequestMessage NewRequest(string? token)
	{
		var request = endpoint.Request(HttpMethod.Post, "c", "/");
		request.Headers.UserAgent.ParseAdd("osu!");
		if (token is not null) request.Headers.Add("osu-token", token);
		return request;
	}
}

/// <summary>The answer to a poll.</summary>
/// <param name="Status">The HTTP status code.</param>
/// <param name="Packets">The packets of the answer, in order; empty unless the status is a success.</param>
public sealed record StableExchange(HttpStatusCode Status, IReadOnlyList<StablePacket> Packets)
{
	/// <summary>Whether the status is a success (2xx).</summary>
	public bool IsSuccess => (int)Status is >= 200 and < 300;
}

/// <summary>The answer to a sign-in.</summary>
/// <param name="Status">The HTTP status code.</param>
/// <param name="Accepted">Whether the server started a session.</param>
/// <param name="UserId">The user id, or the negative failure code of a refused sign-in, or 0 when the answer had none.</param>
/// <param name="Packets">The packets of the answer.</param>
public sealed record StableLoginResult(
	HttpStatusCode Status,
	bool Accepted,
	int UserId,
	IReadOnlyList<StablePacket> Packets);

/// <summary>The result of polling until a packet arrives.</summary>
/// <typeparam name="T">The kind of packet waited for.</typeparam>
/// <param name="Status">The status of the last poll.</param>
/// <param name="Match">The packet that matched, or <see langword="null" /> when the wait ended without one.</param>
/// <param name="Packets">Every packet read while waiting, the matching one included.</param>
public sealed record StableWait<T>(HttpStatusCode Status, T? Match, IReadOnlyList<StablePacket> Packets)
	where T : StablePacket;