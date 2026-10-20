using Basil.Application.Objects.Chat;
using Basil.Domain.Chat;

namespace Basil.Application.Objects.Multiplayer.Rooms;

/// <summary>A multiplayer room's chat channel while the room is open.</summary>
public sealed class RoomChannelSession : ChannelSession
{
	internal RoomChannelSession(Room room, ChannelContext context) : base(context)
	{
		Room = room;
		Channel = new RoomChannel(room.Id, room.Match);
	}

	/// <summary>Gets the room that owns this channel.</summary>
	public Room Room { get; }

	/// <summary>Gets the room's channel.</summary>
	public override RoomChannel Channel { get; }
}
