using Basil.Application.Services.Contracts.Chat;
using Basil.Domain.Chat;

namespace Basil.Application.Services.Contracts.Multiplayer.Rooms;

/// <summary>A multiplayer room's chat channel while the room is open.</summary>
public sealed class RoomChannelSession(Room room) : ChannelSession
{
	/// <summary>Gets the room that owns this channel.</summary>
	public Room Room => room;

	/// <summary>Gets the room's channel.</summary>
	public override RoomChannel Channel { get; } = new(room.Id, room.Match);
}
