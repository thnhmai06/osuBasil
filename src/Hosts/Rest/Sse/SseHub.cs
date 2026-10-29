using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Basil.Application.Content.Persistence;
using Basil.Application.Users.Connections;
using Basil.Application.Users.Users;
using Basil.Host.Rest.Http;
using Microsoft.Extensions.Hosting;

namespace Basil.Host.Rest.Sse;

/// <summary>One event a root publishes; <paramref name="Id" /> becomes the SSE <c>id:</c> of the bundle that carries it.</summary>
/// <param name="Data">The payload, serialized once into the bundle.</param>
/// <param name="Id">The event id, or <see langword="null" /> for none.</param>
public sealed record SseEvent(object Data, string? Id = null);

/// <summary>What a root's poll found since the last second.</summary>
/// <param name="Events">The new events.</param>
/// <param name="Complete">Whether the root is over and its streams end after these events.</param>
public sealed record SsePoll(IReadOnlyList<SseEvent> Events, bool Complete = false);

/// <summary>One open SSE response: a bounded queue of ready-to-send chunks tied to the connection that opened it.</summary>
public sealed class SseStream
{
	private const int Capacity = 8;
	private const int MaxLag = 3;
	private readonly Channel<byte[]> _channel = Channel.CreateBounded<byte[]>(
		new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
	private int _lag;

	internal SseStream(string key, ApiConnection? connection, string owner)
	{
		Key = key;
		Connection = connection;
		Owner = owner;
	}

	/// <summary>Gets the root the stream follows.</summary>
	public string Key { get; }

	/// <summary>Gets the Api connection whose token opened the stream, or <see langword="null" /> for a public stream.</summary>
	public ApiConnection? Connection { get; }

	internal string Owner { get; }

	/// <summary>Gets the chunks to send, in order; it ends when the stream is over.</summary>
	public ChannelReader<byte[]> Reader => _channel.Reader;

	internal void Offer(byte[] chunk)
	{
		// A reader that stays behind for several bundles in a row is cut off; reconnecting gives a new snapshot.
		if (_channel.Reader.Count >= Capacity)
		{
			if (++_lag >= MaxLag) Complete();
			return;
		}

		_lag = 0;
		_channel.Writer.TryWrite(chunk);
	}

	internal void Complete()
	{
		_channel.Writer.TryComplete();
	}
}

/// <summary>
///     The one engine of every SSE route: roots collect events, and once a second the hub bundles what each root
///     collected, serializes it once and hands the same bytes to every stream that follows the root.
/// </summary>
public sealed class SseHub(TimeProvider clock, UserRegistry registry, ISettingsRepository settings) : IHostedService
{
	private static readonly byte[] KeepAlive = ": keep-alive\n\n"u8.ToArray();
	private readonly object _sync = new();
	private readonly Dictionary<string, Root> _roots = new();
	private ITimer? _timer;
	private volatile bool _stopping;

	/// <summary>Gets a value that indicates whether the server is shutting down and takes no new streams.</summary>
	public bool Stopping => _stopping;

	/// <summary>Gets how many streams are open.</summary>
	public int OpenStreams
	{
		get
		{
			lock (_sync)
			{
				return _roots.Values.Sum(root => root.Streams.Count);
			}
		}
	}

	/// <inheritdoc />
	public Task StartAsync(CancellationToken cancellationToken)
	{
		_timer = clock.CreateTimer(_ => Tick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task StopAsync(CancellationToken cancellationToken)
	{
		Announce(false);
		return Task.CompletedTask;
	}

	/// <summary>Stops taking new streams and makes the API answer new requests with 503, without touching the streams that are open.</summary>
	public void StopAccepting()
	{
		_stopping = true;
	}

	/// <summary>
	///     Tells every open stream that the server is going away with one final <c>server</c> event, at once and not at the
	///     next second, and ends the streams.
	/// </summary>
	/// <param name="restarting">Whether the server comes back, which the event says as <c>restarting</c> or <c>shuttingDown</c>.</param>
	public void Announce(bool restarting)
	{
		_stopping = true;
		_timer?.Dispose();
		Root[] roots;
		lock (_sync)
		{
			roots = [.. _roots.Values];
		}

		var state = restarting ? "restarting" : "shuttingDown";
		var final = Frame("server", null, Encoding.UTF8.GetBytes($"{{\"state\":\"{state}\"}}"));
		foreach (var root in roots)
		foreach (var stream in root.Streams.ToArray())
		{
			stream.Offer(final);
			stream.Complete();
		}
	}

	/// <summary>Gives an event to the root with a key; nothing happens when nobody follows it.</summary>
	/// <param name="key">The root's key.</param>
	/// <param name="data">The event.</param>
	/// <param name="id">The SSE id of the event, if it has one.</param>
	public void Publish(string key, object data, string? id = null)
	{
		Root? root;
		lock (_sync)
		{
			_roots.TryGetValue(key, out root);
		}

		root?.Buffer.Enqueue(new SseEvent(data, id));
	}

	/// <summary>Gets how many events a root has collected since its last bundle.</summary>
	/// <param name="key">The root's key.</param>
	/// <returns>The number of events waiting, or 0 when nobody follows the root.</returns>
	internal int Pending(string key)
	{
		lock (_sync)
		{
			return _roots.TryGetValue(key, out var root) ? root.Buffer.Count : 0;
		}
	}

	/// <summary>Ends the streams of one connection on a root, or every stream of the root.</summary>
	/// <param name="key">The root's key.</param>
	/// <param name="connection">The connection whose streams end, or <see langword="null" /> for all of them.</param>
	public void Close(string key, Connection? connection = null)
	{
		Root? root;
		lock (_sync)
		{
			_roots.TryGetValue(key, out root);
		}

		if (root is null) return;

		foreach (var stream in root.Streams.ToArray())
			if (connection is null || ReferenceEquals(stream.Connection, connection))
				stream.Complete();
	}

	/// <summary>Opens a stream on a root; the root is created when it is the first follower.</summary>
	/// <param name="key">The root's key.</param>
	/// <param name="connection">The Api connection of the caller, or <see langword="null" /> when the route is public.</param>
	/// <param name="ip">The address of the caller.</param>
	/// <param name="snapshot">Builds the full state of the root.</param>
	/// <param name="cancellationToken">A token that cancels the opening.</param>
	/// <param name="poll">
	///     Reads what changed in the root since the last second, for a root that has no feed of events; used when the
	///     stream is the first follower of the root.
	/// </param>
	/// <returns>The stream and its snapshot chunk, or why none was opened.</returns>
	public async Task<(SseStream? Stream, byte[]? Snapshot, IResult? Refusal)> OpenAsync(string key,
		ApiConnection? connection, IPAddress ip, Func<CancellationToken, Task<(object Data, string? Id)>> snapshot,
		CancellationToken cancellationToken, Func<CancellationToken, Task<SsePoll>>? poll = null)
	{
		if (_stopping) return (null, null, ShuttingDown());

		var limit = (await settings.GetAsync(cancellationToken)).RateLimits.Connections.PerIp;
		var owner = connection is null ? "ip:" + ip : "user:" + connection.Record.Id;
		SseStream stream;
		Root root;
		lock (_sync)
		{
			var open = _roots.Values.SelectMany(r => r.Streams).Count(s => s.Owner == owner || s.Owner == "ip:" + ip);
			if (open >= limit)
				return (null, null,
					Api.Error(StatusCodes.Status429TooManyRequests, "rateLimited", "Too many open streams."));

			if (!_roots.TryGetValue(key, out root!)) _roots[key] = root = new Root(key) { Poll = poll };
			stream = new SseStream(key, connection, owner);
			root.Streams.Add(stream);
		}

		try
		{
			var (data, id) = await root.SnapshotAsync(snapshot, Volatile.Read(ref _tick), cancellationToken);
			return (stream, Frame("snapshot", id, data), null);
		}
		catch
		{
			Release(stream);
			throw;
		}
	}

	/// <summary>Takes a stream off its root; the last stream to leave removes the root and its buffer.</summary>
	/// <param name="stream">The stream that ended.</param>
	public void Release(SseStream stream)
	{
		stream.Complete();
		lock (_sync)
		{
			if (!_roots.TryGetValue(stream.Key, out var root)) return;

			root.Streams.Remove(stream);
			if (root.Streams.Count == 0) _roots.Remove(stream.Key);
		}
	}

	/// <summary>Marks the connection of a stream active, so a stream that is open keeps its session from idling out.</summary>
	/// <param name="stream">The stream.</param>
	public void Touch(SseStream stream)
	{
		if (stream.Connection is { IsOpen: true } connection) registry.MarkActive(connection);
	}

	internal static byte[] Frame(string eventName, string? id, byte[] json)
	{
		var head = Encoding.UTF8.GetBytes((id is null ? "" : $"id: {id}\n") + $"event: {eventName}\nretry: 3000\ndata: ");
		var frame = new byte[head.Length + json.Length + 2];
		head.CopyTo(frame, 0);
		json.CopyTo(frame, head.Length);
		frame[^2] = (byte)'\n';
		frame[^1] = (byte)'\n';
		return frame;
	}

	private IResult ShuttingDown()
	{
		return Api.Error(StatusCodes.Status503ServiceUnavailable, "shuttingDown", "The server is shutting down.");
	}

	private long _tick;

	/// <summary>Runs one second of the hub: bundles each root's events and ships them to its streams.</summary>
	internal void Tick()
	{
		if (Interlocked.Exchange(ref _ticking, 1) == 1) return;

		_ = TickAsync();
	}

	private int _ticking;

	private async Task TickAsync()
	{
		try
		{
			Interlocked.Increment(ref _tick);
			Root[] roots;
			lock (_sync)
			{
				roots = [.. _roots.Values];
			}

			foreach (var root in roots)
			{
				var complete = false;
				if (root.Poll is { } poll)
					try
					{
						var found = await poll(CancellationToken.None);
						foreach (var item in found.Events) root.Buffer.Enqueue(item);
						complete = found.Complete;
					}
					catch (Exception exception) when (exception is not OperationCanceledException)
					{
						complete = true; // a root that cannot be read ends its streams; reconnecting starts afresh
					}

				var events = new List<SseEvent>();
				while (root.Buffer.TryDequeue(out var item)) events.Add(item);

				var chunk = events.Count == 0
					? KeepAlive
					: Frame("bundle", events[^1].Id, JsonSerializer.SerializeToUtf8Bytes(events.Select(e => e.Data), Api.Json));
				foreach (var stream in root.Streams.ToArray())
				{
					if (stream.Connection is { IsOpen: false })
					{
						stream.Complete();
						continue;
					}

					stream.Offer(chunk);
					if (complete) stream.Complete();
				}
			}
		}
		finally
		{
			Volatile.Write(ref _ticking, 0);
		}
	}

	private sealed class Root(string key)
	{
		private readonly SemaphoreSlim _snapshotLock = new(1, 1);
		private byte[]? _snapshot;
		private string? _snapshotId;
		private long _snapshotTick = -1;

		public string Key { get; } = key;

		public Func<CancellationToken, Task<SsePoll>>? Poll { get; init; }

		public ConcurrentQueue<SseEvent> Buffer { get; } = new();

		public List<SseStream> Streams { get; } = [];

		/// <summary>Builds the snapshot once for every stream that joins during the same second.</summary>
		public async Task<(byte[] Data, string? Id)> SnapshotAsync(
			Func<CancellationToken, Task<(object Data, string? Id)>> build, long tick, CancellationToken cancellationToken)
		{
			await _snapshotLock.WaitAsync(cancellationToken);
			try
			{
				if (_snapshot is null || _snapshotTick != tick)
				{
					var (data, id) = await build(cancellationToken);
					_snapshot = JsonSerializer.SerializeToUtf8Bytes(data, Api.Json);
					_snapshotId = id;
					_snapshotTick = tick;
				}

				return (_snapshot, _snapshotId);
			}
			finally
			{
				_snapshotLock.Release();
			}
		}
	}
}
