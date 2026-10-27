using Basil.Application.Users.Connections;
using Basil.Application.Multiplayer.Runtime.Rooms;
using Basil.Domain.Users;

namespace Basil.Application.Chat.Runtime;

/// <summary>A multiplayer room's chat channel while the room is open.</summary>
public sealed class RoomChannel : Channel
{
	internal RoomChannel(Room room, ChannelContext context) : base(context)
	{
		Room = room;
	}

	/// <summary>Gets the room this channel belongs to.</summary>
	public Room Room { get; }

	/// <inheritdoc />
	public override string Name => $"#mp_{Room.Id}";

	/// <inheritdoc />
	public override string Topic => Room.Match.Value.Name;

	/// <inheritdoc />
	internal override bool CanRead(Connection connection, DateTimeOffset now)
	{
		return Room.Involves(connection, now) ||
		       connection.User.EffectivePermissions(now).Allows(UserPermissions.RoomObserve);
	}

	/// <inheritdoc />
	internal override bool CanWrite(Connection connection, DateTimeOffset now)
	{
		return Room.Involves(connection, now) ||
		       connection.User.EffectivePermissions(now).Allows(UserPermissions.ChatPostAnyRoom);
	}
}