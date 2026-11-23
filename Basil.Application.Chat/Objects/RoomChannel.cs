using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Chat;
using Basil.Domain.Users;

namespace Basil.Application.Chat.Objects;

public sealed class RoomChannel : Channel
{
	internal RoomChannel(Room room, ChannelContext context) : base(context)
	{
		Room = room;
	}

	public Room Room { get; }

	public override string Name => RoomChannelRules.NameFor(Room.Id);

	public override string Topic => Room.Match.Value.Name;

	internal override bool CanRead(Session connection, UserPrivileges privileges)
	{
		return Room.Involves(connection, privileges) || privileges.Allows(UserPrivileges.Tournament);
	}

	internal override bool CanWrite(Session connection, UserPrivileges privileges)
	{
		return Room.Involves(connection, privileges) || privileges.Allows(UserPrivileges.Tournament);
	}
}