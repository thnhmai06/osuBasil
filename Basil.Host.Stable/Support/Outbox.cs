using Basil.Application.Sessions.Registry;
using System.Collections.Concurrent;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Sessions;
using Basil.Protocol.Game.Stable.Wire.Packets;
using Session = Basil.Application.Sessions.Objects.Session;

namespace Basil.Host.Stable.Support;

internal sealed class Outbox(SessionRegistry registry)
{
	public const int MaxQueuedPackets = 10_000;

	public const long MaxQueuedBytes = 4 * 1024 * 1024;

	private readonly ConcurrentDictionary<Session, Pending> _queues = new(ReferenceEqualityComparer.Instance);

	public event Action<Session>? Polled;

	public bool HasPending(Session connection)
	{
		return _queues.TryGetValue(connection, out var pending) && !pending.Queue.IsEmpty;
	}

	public void Send(IEnumerable<Session> recipients, Action<PacketWriter> write)
	{
		byte[]? bytes = null;
		List<Session>? tooSlow = null;
		foreach (var recipient in recipients.Where(Receives))
		{
			bytes ??= Build(write);
			var pending = _queues.GetOrAdd(recipient, _ => new Pending());
			pending.Queue.Enqueue(bytes);
			if (Interlocked.Add(ref pending.Bytes, bytes.Length) > MaxQueuedBytes ||
			    pending.Queue.Count > MaxQueuedPackets)
				(tooSlow ??= []).Add(recipient);
		}

		// Closed after the loop: the recipients may be a collection that closing a connection changes.
		if (tooSlow is null) return;
		foreach (var connection in tooSlow)
		{
			_queues.TryRemove(connection, out _);
			registry.Close(connection, SessionEndReason.ConnectionLost);
		}
	}

	public void Send(Session recipient, Action<PacketWriter> write)
	{
		Send([recipient], write);
	}

	public byte[] Drain(Session connection)
	{
		if (!_queues.TryGetValue(connection, out var pending))
		{
			Polled?.Invoke(connection);
			return [];
		}

		using var stream = new MemoryStream();
		while (pending.Queue.TryDequeue(out var bytes))
		{
			stream.Write(bytes);
			Interlocked.Add(ref pending.Bytes, -bytes.Length);
		}

		Polled?.Invoke(connection);
		return stream.ToArray();
	}

	public void Remove(Session connection)
	{
		_queues.TryRemove(connection, out _);
	}

	public static byte[] Build(Action<PacketWriter> write)
	{
		using var stream = new MemoryStream();
		write(new PacketWriter(stream));
		return stream.ToArray();
	}

	private static bool Receives(Session connection)
	{
		// The bot's game client connection has no client to drain a queue, so it would overflow.
		return connection is { IsOpen: true, IsServerOwned: false } and (StableSession or TourneySession);
	}

	private sealed class Pending
	{
		public readonly ConcurrentQueue<byte[]> Queue = new();

		public long Bytes;
	}
}