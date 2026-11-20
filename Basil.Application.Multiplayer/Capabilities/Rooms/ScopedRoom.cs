using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Multiplayer.Room;
using Basil.Domain.Users;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Application.Multiplayer.Capabilities.Rooms;

public readonly struct ScopedRoom
{
	private readonly UserPrivileges _global;

	public Session Actor { get; }

	public RoomPrivileges Privileges { get; }

	internal ScopedRoom(Room room, Session actor) : this(room, actor, Snapshot(room, actor))
	{
	}

	private ScopedRoom(Room room, Session actor,
		(UserPrivileges Global, RoomPrivileges Privileges) snapshot)
	{
		Actor = actor;
		Privileges = snapshot.Privileges;
		Room = room;
		_global = snapshot.Global;
		ManagesAnyRoom = _global.Allows(UserPrivileges.Tournament);
	}

	public Room Room { get; }

	public ScopedRoomSlots Slots => new(this);

	public ScopedRoomRounds Rounds => new(this);

	public ScopedRoomSettings Settings => new(this);

	public ScopedRoomAuthority Authority => new(this);

	public ScopedRoomMembers Members => new(this);

	internal bool ManagesAnyRoom { get; }

	private static (UserPrivileges Global, RoomPrivileges Privileges) Snapshot(Room room, Session actor)
	{
		var global = actor.EffectivePrivileges(room.Context.Clock.GetUtcNow());

		// Managing every room acts with the creator's authority, joined or not. Otherwise a user without the player
		// privilege has no room privileges.
		var privileges = global.Allows(UserPrivileges.Tournament)
			? RoomPrivileges.Creator
			: global.Allows(UserPrivileges.Player)
				? RoleGroups(room, actor)
				: RoomPrivileges.Viewer;

		return (global, privileges);
	}

	private static RoomPrivileges RoleGroups(Room room, Session actor)
	{
		var privileges = RoomPrivileges.Viewer;

		// Player: seated StableSession
		if (actor is StableSession stable && room.SlotOf(stable) is not null)
			privileges |= RoomPrivileges.Player;

		// Host: reference equals
		if (ReferenceEquals(actor, room.Host))
			privileges |= RoomPrivileges.Host;

		// Referee: managed by but not creator
		if (room.IsManagedBy(actor.Record) && !Equals(room.Creator, actor.Record))
			privileges |= RoomPrivileges.Referee;

		// Creator: creator equals
		if (Equals(room.Creator, actor.Record))
			privileges |= RoomPrivileges.Creator;

		return privileges;
	}

	// Succeeds when the caller holds at least one of the allowed roles.
	internal RoomActionResult? Require(RoomPrivileges allowed)
	{
		if ((Privileges & allowed) != 0)
			return null;

		return RoomActionResult.Denied;
	}

	public bool Involves => Room.Involves(Actor, _global);

	internal RoomActionResult? RequireJoin()
	{
		return _global.Allows(UserPrivileges.Player)
			? null
			: RoomActionResult.Denied;
	}

	public Task<RoomActionResult> CloseAsync()
	{
		return Task.FromResult(Require(RoomPrivileges.Moderators) ?? Room.Close(Actor.Record));
	}
}