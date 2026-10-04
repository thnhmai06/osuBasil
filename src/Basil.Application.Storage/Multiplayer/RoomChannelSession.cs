using Basil.Application.Storage.Chat;
using Basil.Domain.Chat;

namespace Basil.Application.Storage.Multiplayer;

/// <summary>A multiplayer room's chat channel while the room is open.</summary>
public sealed class RoomChannelSession(Room room) : ChannelSession(new RoomChannel(room.Id, room.Match))
{
	/// <summary>Gets the room that owns this channel.</summary>
	public Room Room => room;
}