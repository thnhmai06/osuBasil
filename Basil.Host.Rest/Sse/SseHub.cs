using Basil.Application.Sessions.Messages;
using Basil.Application.Sessions.Registry;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Basil.Application.Shared.Settings;
using Basil.Application.Sessions.Objects;
using Basil.Host.Rest.Http;
using Mediator;
using Microsoft.Extensions.Hosting;

namespace Basil.Host.Rest.Sse;

public sealed record SseEvent(object Data, string? Id = null);

public sealed record SsePoll(IReadOnlyList<SseEvent> Events, bool Complete = false);

public sealed class SseStream
{
	private const int Capacity = 8;
	private const int MaxLag = 3;

	private readonly Channel<byte[]> _channel = Channel.CreateBounded<byte[]>(
		new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

	private int _lag;
	private int _released;

	internal SseStream(string key, ApiSession? connection, string? userKey)
	{
		Key = key;
		Session = connection;
		UserKey = userKey;
	}

	public string Key { get; }

	public ApiSession? Session { get; }

	internal string? UserKey { get; }

	public ChannelReader<byte[]> Reader => _channel.Reader;

	internal bool TryRelease()
	{
		return Interlocked.Exchange(ref _released, 1) == 0;
	}

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

public sealed class SseHub(TimeProvider clock, SessionRegistry registry, ISender sender) : IHostedService
{
	private static readonly byte[] KeepAlive = ": keep-alive\n\n"u8.ToArray();
	private readonly ConcurrentDictionary<string, int> _open = new();
	private readonly ConcurrentDictionary<string, Root> _roots = new();
	private volatile bool _stopping;

	private long _tick;

	private int _ticking;
	private ITimer? _timer;

	public bool Stopping => _stopping;

	public int OpenStreams => _roots.Sum(pair => pair.Value.Count);

	public Task StartAsync(CancellationToken ct)
	{
		_timer = clock.CreateTimer(_ => Tick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken ct)
	{
		Announce();
		return Task.CompletedTask;
	}

	public void StopAccepting()
	{
		_stopping = true;
	}

	public void Announce()
	{
		_stopping = true;
		_timer?.Dispose();
		Root[] roots = [.. _roots.Values];
		var final = Frame("server", null, Encoding.UTF8.GetBytes("{\"state\":\"shuttingDown\"}"));
		foreach (var root in roots)
		foreach (var stream in root.Snapshot())
		{
			stream.Offer(final);
			stream.Complete();
			Release(stream);
		}
	}

	public void Publish(string key, object data, string? id = null)
	{
		if (_roots.TryGetValue(key, out var root)) root.Buffer.Enqueue(new SseEvent(data, id));
	}

	internal int Pending(string key)
	{
		return _roots.TryGetValue(key, out var root) ? root.Buffer.Count : 0;
	}

	public void Close(string key, Session? connection = null)
	{
		if (!_roots.TryGetValue(key, out var root)) return;

		foreach (var stream in root.Snapshot())
			if (connection is null || ReferenceEquals(stream.Session, connection))
				stream.Complete();
	}

	public async Task<(SseStream? Stream, byte[]? Snapshot, IResult? Refusal)> OpenAsync(string key,
		ApiSession? connection, Func<CancellationToken, Task<(object Data, string? Id)>> snapshot,
		CancellationToken ct, Func<CancellationToken, Task<SsePoll>>? poll = null)
	{
		if (_stopping) return (null, null, ShuttingDown());

		var current = await sender.Send(new ReadSettings(), ct);
		var limit = current.RateLimits.Connections.PerIp;
		var userKey = connection is null ? null : "user:" + connection.Record.Id;
		if (userKey is not null && !TryCount(userKey, limit)) return (null, null, TooManyStreams());

		var stream = new SseStream(key, connection, userKey);
		Root root;
		do
		{
			root = _roots.GetOrAdd(key, _ => new Root { Poll = poll });
			// A root that lost its last stream refuses new ones; it is gone from the table in a moment.
		} while (!root.Add(stream));

		try
		{
			var (data, id) = await root.SnapshotAsync(snapshot, Volatile.Read(ref _tick), ct);
			return (stream, Frame("snapshot", id, data), null);
		}
		catch
		{
			Release(stream);
			throw;
		}
	}

	public void Release(SseStream stream)
	{
		stream.Complete();
		if (!stream.TryRelease()) return;

		if (_roots.TryGetValue(stream.Key, out var root) && root.Remove(stream) && root.TryRetire())
			_roots.TryRemove(new KeyValuePair<string, Root>(stream.Key, root));

		if (stream.UserKey is not null) Uncount(stream.UserKey);
	}

	private bool TryCount(string owner, int limit)
	{
		if (_open.AddOrUpdate(owner, 1, (_, count) => count + 1) <= limit) return true;

		Uncount(owner);
		return false;
	}

	private void Uncount(string owner)
	{
		var left = _open.AddOrUpdate(owner, 0, (_, count) => count - 1);
		if (left <= 0) _open.TryRemove(new KeyValuePair<string, int>(owner, left));
	}

	private static IResult TooManyStreams()
	{
		return Api.Error(StatusCodes.Status429TooManyRequests, "rateLimited", "Too many open streams.");
	}

	public void Touch(SseStream stream)
	{
		if (stream.Session is { IsOpen: true } connection) sender.Send(new MarkSessionActive(connection));
	}

	internal static byte[] Frame(string eventName, string? id, byte[] json)
	{
		var head = Encoding.UTF8.GetBytes(
			(id is null ? "" : $"id: {id}\n") + $"event: {eventName}\nretry: 3000\ndata: ");
		var frame = new byte[head.Length + json.Length + 2];
		head.CopyTo(frame, 0);
		json.CopyTo(frame, head.Length);
		frame[^2] = (byte)'\n';
		frame[^1] = (byte)'\n';
		return frame;
	}

	private static IResult ShuttingDown()
	{
		return Api.Error(StatusCodes.Status503ServiceUnavailable, "shuttingDown", "The server is shutting down.");
	}

	internal void Tick()
	{
		if (Interlocked.Exchange(ref _ticking, 1) == 1) return;

		_ = TickAsync();
	}

	private async Task TickAsync()
	{
		try
		{
			Interlocked.Increment(ref _tick);
			foreach (var root in _roots.Values)
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
					: Frame("bundle", events[^1].Id,
						JsonSerializer.SerializeToUtf8Bytes(events.Select(e => e.Data), Api.Json));
				foreach (var stream in root.Snapshot())
				{
					if (stream.Session is { IsOpen: false })
					{
						Release(stream);
						continue;
					}

					stream.Offer(chunk);
					if (complete) Release(stream);
				}
			}
		}
		finally
		{
			Volatile.Write(ref _ticking, 0);
		}
	}

	private sealed class Root
	{
		private readonly List<SseStream> _list = [];
		private readonly SemaphoreSlim _snapshotLock = new(1, 1);
		private readonly Lock _streams = new();
		private bool _retired;
		private byte[]? _snapshot;
		private string? _snapshotId;
		private long _snapshotTick = -1;

		public Func<CancellationToken, Task<SsePoll>>? Poll { get; init; }

		public ConcurrentQueue<SseEvent> Buffer { get; } = new();

		public int Count
		{
			get
			{
				lock (_streams)
				{
					return _list.Count;
				}
			}
		}

		public bool Add(SseStream stream)
		{
			lock (_streams)
			{
				if (_retired) return false;

				_list.Add(stream);
				return true;
			}
		}

		public bool Remove(SseStream stream)
		{
			lock (_streams)
			{
				return _list.Remove(stream);
			}
		}

		public bool TryRetire()
		{
			lock (_streams)
			{
				if (_list.Count != 0) return false;

				_retired = true;
				return true;
			}
		}

		public SseStream[] Snapshot()
		{
			lock (_streams)
			{
				return [.. _list];
			}
		}

		public async Task<(byte[] Data, string? Id)> SnapshotAsync(
			Func<CancellationToken, Task<(object Data, string? Id)>> build, long tick,
			CancellationToken ct)
		{
			await _snapshotLock.WaitAsync(ct);
			try
			{
				if (_snapshot is null || _snapshotTick != tick)
				{
					var (data, id) = await build(ct);
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