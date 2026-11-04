using System.Collections.Concurrent;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Registry;
using Basil.Domain.Authentication;
using Basil.Protocol.Stable.Wire.Packets;

namespace Basil.Host.Stable.Support;

/// <summary>
///     Holds the packets waiting for each osu! client until its next poll; a client that lets more than
///     <see cref="MaxQueuedPackets" /> writes or <see cref="MaxQueuedBytes" /> bytes pile up is disconnected.
/// </summary>
internal sealed class Outbox(UserRegistry registry)
{
	/// <summary>The most writes that may wait for one client.</summary>
	public const int MaxQueuedPackets = 10_000;

	/// <summary>The most bytes that may wait for one client.</summary>
	public const long MaxQueuedBytes = 4 * 1024 * 1024;

	private readonly ConcurrentDictionary<Connection, Pending> _queues = new(ReferenceEqualityComparer.Instance);

	/// <summary>Occurs when a connection polls and takes what was queued for it.</summary>
	public event Action<Connection>? Polled;

	/// <summary>Tells whether packets are waiting for a connection.</summary>
	/// <param name="connection">The connection.</param>
	public bool HasPending(Connection connection)
	{
		return _queues.TryGetValue(connection, out var pending) && !pending.Queue.IsEmpty;
	}

	/// <summary>Builds the packets once and queues them for every open osu! client among the recipients.</summary>
	/// <param name="recipients">The connections to deliver to; connections that are not osu! clients are skipped.</param>
	/// <param name="write">Writes the packets.</param>
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

	/// <summary>Queues packets for one connection.</summary>
	/// <param name="recipient">The connection to deliver to.</param>
	/// <param name="write">Writes the packets.</param>
	public void Send(Connection recipient, Action<PacketWriter> write)
	{
		Send([recipient], write);
	}

	/// <summary>Takes the queued packets of a connection.</summary>
	/// <param name="connection">The connection that polls.</param>
	/// <returns>The packets, concatenated in the order they were queued.</returns>
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

	/// <summary>Forgets a connection that closed.</summary>
	/// <param name="connection">The closed connection.</param>
	public void Remove(Connection connection)
	{
		_queues.TryRemove(connection, out _);
	}

	/// <summary>Writes packets into a byte array.</summary>
	/// <param name="write">Writes the packets.</param>
	/// <returns>The wire bytes.</returns>
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