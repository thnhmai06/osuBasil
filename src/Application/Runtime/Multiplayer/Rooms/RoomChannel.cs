using Basil.Application.Runtime.Chat;

namespace Basil.Application.Runtime.Multiplayer.Rooms;

/// <summary>A multiplayer room's chat channel while the room is open.</summary>
public sealed class RoomChannel : Channel
{
	internal RoomChannel(Room room, ChannelContext context) : base(context)
	{
		Room = room;
	}

	/// <summary>Gets the room that owns this channel.</summary>
	public Room Room { get; }

	/// <inheritdoc />
	public override string Name => $"#mp_{Room.Id}";

	/// <inheritdoc />
	public override string Topic => Room.Match.Value.Name;
}