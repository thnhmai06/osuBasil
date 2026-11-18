using System.Collections.Concurrent;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Registry;
using Basil.Domain.Authentication;
using Basil.Protocol.Game.Stable.Wire.Packets;

namespace Basil.Host.Stable.Support;

internal sealed class Outbox(UserRegistry registry)
{
	public const int MaxQueuedPackets = 10_000;

	public const long MaxQueuedBytes = 4 * 1024 * 1024;

	private readonly ConcurrentDictionary<Connection, Pending> _queues = new(ReferenceEqualityComparer.Instance);

	public event Action<Connection>? Polled;

	public bool HasPending(Connection connection)
	{
		return _queues.TryGetValue(connection, out var pending) && !pending.Queue.IsEmpty;
	}

	public void Send(IEnumerable<Connection> recipients, Action<PacketWriter> write)
	{
		byte[]? bytes = null;
		List<Connection>? tooSlow = null;
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

	public void Send(Connection recipient, Action<PacketWriter> write)
	{
		Send([recipient], write);
	}

	public byte[] Drain(Connection connection)
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

	public void Remove(Connection connection)
	{
		_queues.TryRemove(connection, out _);
	}

	public static byte[] Build(Action<PacketWriter> write)
	{
		using var stream = new MemoryStream();
		write(new PacketWriter(stream));
		return stream.ToArray();
	}

	private static bool Receives(Connection connection)
	{
		// The bot's game client connection has no client to drain a queue, so it would overflow.
		return connection is { IsOpen: true, IsServerOwned: false } and (StableConnection or TourneyConnection);
	}

	private sealed class Pending
	{
		public readonly ConcurrentQueue<byte[]> Queue = new();

		public long Bytes;
	}
}