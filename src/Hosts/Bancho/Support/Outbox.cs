using System.Collections.Concurrent;
using Basil.Application.Users.Objects.Connections;
using Basil.Protocol.Bancho.Wire.Packets;

namespace Basil.Host.Bancho.Support;

/// <summary>Holds the packets waiting for each osu! client until its next poll.</summary>
internal sealed class Outbox
{
	private readonly ConcurrentDictionary<Connection, ConcurrentQueue<byte[]>> _queues =
		new(ReferenceEqualityComparer.Instance);

	/// <summary>Occurs when a connection polls and takes what was queued for it.</summary>
	public event Action<Connection>? Polled;

	/// <summary>Tells whether packets are waiting for a connection.</summary>
	/// <param name="connection">The connection.</param>
	public bool HasPending(Connection connection)
	{
		return _queues.TryGetValue(connection, out var queue) && !queue.IsEmpty;
	}

	/// <summary>Builds the packets once and queues them for every open osu! client among the recipients.</summary>
	/// <param name="recipients">The connections to deliver to; connections that are not osu! clients are skipped.</param>
	/// <param name="write">Writes the packets.</param>
	public void Send(IEnumerable<Connection> recipients, Action<PacketWriter> write)
	{
		byte[]? bytes = null;
		foreach (var recipient in recipients.Where(Receives))
		{
			bytes ??= Build(write);
			_queues.GetOrAdd(recipient, _ => new ConcurrentQueue<byte[]>()).Enqueue(bytes);
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
		if (!_queues.TryGetValue(connection, out var queue))
		{
			Polled?.Invoke(connection);
			return [];
		}

		using var stream = new MemoryStream();
		while (queue.TryDequeue(out var bytes)) stream.Write(bytes);
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
		return connection is { IsOpen: true } and (BanchoConnection or TourneyConnection);
	}
}
