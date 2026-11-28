using System.Collections.Concurrent;
using Basil.Application.Chat.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;

namespace Basil.Application.Chat.Registry;

public sealed class RoomChannels
{
	private readonly ConcurrentDictionary<Room, RoomChannel> _channels = new(ReferenceEqualityComparer.Instance);
	private readonly ChannelContext _context;

	internal RoomChannels(ChannelContext context)
	{
		_context = context;
	}

	internal IEnumerable<RoomChannel> All => _channels.Values;

	public RoomChannel Of(Room room)
	{
		if (room.IsClosed) return Closed(room);

		var channel =
			_channels.GetOrAdd(room, static (current, context) => new RoomChannel(current, context), _context);
		if (!room.IsClosed) return channel;

		// The room closed after the check, and its close event may already have been handled: do not keep a channel
		// nobody will remove.
		Remove(room)?.Close();
		return Closed(room);
	}

	private RoomChannel Closed(Room room)
	{
		return new RoomChannel(room, _context) { IsClosed = true };
	}

	internal RoomChannel? Find(Room room)
	{
		return _channels.GetValueOrDefault(room);
	}

	internal RoomChannel? Remove(Room room)
	{
		return _channels.TryRemove(room, out var channel) ? channel : null;
	}
}