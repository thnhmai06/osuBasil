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

	/// <summary>Removes a session from the channel unless its user still manages the room.</summary>
	internal void Release(Session member)
	{
		if (!Room.IsManagedBy(member.Record)) ScopeTo(member).Part();
	}

	/// <summary>Removes every session of a former referee that no longer has a place in the room.</summary>
	internal void ReleaseReferee(UserRecord referee)
	{
		var now = Context.Clock.GetUtcNow();
		foreach (var member in Members.Where(member => member.Record.Equals(referee) &&
		                                               !Room.Involves(member, member.User.EffectivePrivileges(now))))
			ScopeTo(member).Part();
	}

	internal override bool CanRead(Session connection, UserPrivileges privileges)
	{
		return Room.Involves(connection, privileges);
	}

	internal override bool CanWrite(Session connection, UserPrivileges privileges)
	{
		return Room.Involves(connection, privileges);
	}

	internal override ChannelMemberRole RoleOf(Session member, UserPrivileges privileges)
	{
		return (Room.IsManagedBy(member.Record) ? ChannelMemberRole.Manager : ChannelMemberRole.None) |
		       (Room.Involves(member, privileges) ? ChannelMemberRole.Speaker : ChannelMemberRole.None);
	}
}