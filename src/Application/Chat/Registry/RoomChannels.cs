using System.Collections.Concurrent;
using Basil.Application.Chat.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;

namespace Basil.Application.Chat.Registry;

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

	/// <summary>Gets the channel of an open room.</summary>
	/// <param name="room">The open room.</param>
	/// <returns>The room's chat channel.</returns>
	/// <remarks>The channel's members follow the room's events; a caller right after a change may not see it yet.</remarks>
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