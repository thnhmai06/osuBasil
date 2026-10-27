using System.Collections.Concurrent;
using Basil.Application.Users.Connections;
using Basil.Application.Multiplayer.Runtime.Rooms;

namespace Basil.Application.Chat.Runtime;

/// <summary>The runtime chat channels of multiplayer rooms.</summary>
public sealed class RoomChannels
{
	private readonly ConcurrentDictionary<Room, RoomChannel> _channels = new(ReferenceEqualityComparer.Instance);
	private readonly ChannelContext _context;

	/// <summary>Creates the lookup of room channels.</summary>
	/// <param name="context">The runtime dependencies of the channels it opens.</param>
	internal RoomChannels(ChannelContext context)
	{
		_context = context;
	}

	/// <summary>Gets the channel of an open room, adding its seated players and observers.</summary>
	/// <param name="room">The open room.</param>
	/// <returns>The room's chat channel.</returns>
	public RoomChannel Of(Room room)
	{
		using var scope = room.Enter();
		if (room.IsClosed)
		{
			if (_channels.TryRemove(room, out var closed))
			{
				closed.Close();
				return closed;
			}

			return new RoomChannel(room, _context) { IsClosed = true };
		}

		var channel = _channels.GetOrAdd(room, current => new RoomChannel(current, _context));
		foreach (var player in room.Slots.Select(slot => slot.Player).OfType<BanchoConnection>())
			channel.ScopeTo(player).Join();
		foreach (var observer in room.Members.Observers)
			channel.ScopeTo(observer).Join();

		return channel;
	}

	/// <summary>Gets a room's channel if it has one.</summary>
	/// <param name="room">The room.</param>
	/// <returns>The room's channel, or <see langword="null" /> when it has none.</returns>
	internal RoomChannel? Find(Room room)
	{
		return _channels.GetValueOrDefault(room);
	}

	/// <summary>Removes a room's channel.</summary>
	/// <param name="room">The room whose channel is removed.</param>
	/// <returns>The channel that was removed, or <see langword="null" /> when it had none.</returns>
	internal RoomChannel? Remove(Room room)
	{
		return _channels.TryRemove(room, out var channel) ? channel : null;
	}
}