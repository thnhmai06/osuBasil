using Basil.Application.Chat;
using Basil.Domain.Chat;

namespace Basil.Application.Multiplayer;

/// <summary>A multiplayer room's chat channel while the room is open.</summary>
public sealed class RoomChannelSession(Room room) : ChannelSession(new RoomChannel(room.Id, room.Name))
{
	/// <summary>Gets the room that owns this channel.</summary>
	public Room Room => room;
}