using Basil.Application.Shared;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Shared.Privileges;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Capabilities.Rooms;

/// <summary>A room scoped to a connection, with its room privileges snapshot.</summary>
/// <remarks>
///     The snapshot is taken once, so a scope is for one request or command: create it with
///     <see cref="Objects.Rooms.Room.ScopeTo" />, use it, drop it. Keeping one past the request lets its privileges outlive a
///     change of
///     role or privilege.
/// </remarks>
public readonly struct ScopedRoom
{
	/// <summary>The privileges the connection holds, as they were when the scope was created.</summary>
	private readonly UserPrivileges _global;

	/// <summary>Gets the connection this scope is for.</summary>
	public Connection Actor { get; }

	/// <summary>Gets the room privileges the connection held when the scope was created.</summary>
	public RoomPrivileges Privileges { get; }

	/// <summary>Creates a scoped room for a connection.</summary>
	/// <param name="room">The room to scope.</param>
	/// <param name="actor">The connection acting on the room.</param>
	internal ScopedRoom(Room room, Connection actor) : this(room, actor, Snapshot(room, actor))
	{
	}

	private ScopedRoom(Room room, Connection actor,
		(UserPrivileges Global, RoomPrivileges Privileges) snapshot)
	{
		Actor = actor;
		Privileges = snapshot.Privileges;
		Room = room;
		_global = snapshot.Global;
		ManagesAnyRoom = _global.Allows(UserPrivileges.Tournament);
	}

	/// <summary>The room this scope is over.</summary>
	public Room Room { get; }

	/// <summary>The slots service for this scoped room.</summary>
	public ScopedRoomSlots Slots => new(this);

	/// <summary>The rounds service for this scoped room.</summary>
	public ScopedRoomRounds Rounds => new(this);

	/// <summary>The settings service for this scoped room.</summary>
	public ScopedRoomSettings Settings => new(this);

	/// <summary>The authority service for this scoped room.</summary>
	public ScopedRoomAuthority Authority => new(this);

	/// <summary>The membership operations for this scoped room.</summary>
	public ScopedRoomMembers Members => new(this);

	/// <summary>Whether the connection may manage every room.</summary>
	internal bool ManagesAnyRoom { get; }

	private static (UserPrivileges Global, RoomPrivileges Privileges) Snapshot(Room room, Connection actor)
	{
		var global = actor.EffectivePrivileges(room.Context.Clock.GetUtcNow());

		// Managing every room acts with the creator's authority, joined or not. Otherwise a user without the player
		// privilege has no room privileges.
		var privileges = global.Allows(UserPrivileges.Tournament)
			? RoomPrivileges.Creator
			: global.Allows(UserPrivileges.Player)
				? RoleGroups(room, actor)
				: RoomPrivileges.None;

		return (global, privileges);
	}

	/// <summary>Unites the privilege groups of the roles the actor holds in the room.</summary>
	private static RoomPrivileges RoleGroups(Room room, Connection actor)
	{
		var privileges = RoomPrivileges.None;

		// Player: seated StableConnection
		if (actor is StableConnection stable && room.Slots.Find(stable) is not null)
			privileges |= RoomPrivileges.Player;

		// Host: reference equals
		if (ReferenceEquals(actor, room.Authority.Host))
			privileges |= RoomPrivileges.Host;

		// Referee: managed by but not creator
		if (room.Authority.IsManagedBy(actor.Record) && !Equals(room.Authority.Creator, actor.Record))
			privileges |= RoomPrivileges.Referee;

		// Creator: creator equals
		if (Equals(room.Authority.Creator, actor.Record))
			privileges |= RoomPrivileges.Creator;

		return privileges;
	}

	/// <summary>Checks that the scoped connection holds the room privileges an action needs.</summary>
	/// <param name="needed">The room privileges required.</param>
	/// <returns>
	///     <see langword="null" /> when allowed; a denial when the caller may
	///     not
	///     join rooms at all; otherwise <see cref="RoomOutcome.RoleDenied" />.
	/// </returns>
	internal RoomOutcome? Require(RoomPrivileges needed)
	{
		if (!ScopedPrivileges.Lacks(Privileges, needed))
			return null;

		return !ManagesAnyRoom && RequireJoin() is { } cannotJoin ? cannotJoin : RoomOutcome.RoleDenied;
	}

	/// <summary>
	///     Gets whether the connection takes part in the room: it is seated or observing, it manages the room while
	///     allowed to join rooms, or it manages every room.
	/// </summary>
	public bool Involves => Room.Involves(Actor, _global);

	/// <summary>Checks whether the connection may join rooms.</summary>
	/// <returns><see langword="null" /> when allowed, otherwise a denial.</returns>
	internal RoomOutcome? RequireJoin()
	{
		return _global.Allows(UserPrivileges.Player)
			? null
			: RoomOutcome.Denied;
	}

	/// <summary>Closes the room, evicting its players, ending its round and forgetting it.</summary>
	/// <returns>
	///     Ok, or a denial; a room that has closed already returns Ok and does nothing.
	/// </returns>
	/// <remarks>The caller needs <see cref="RoomPrivileges.RoomClose" />, checked before the room is entered.</remarks>
	public Task<RoomOutcome> CloseAsync()
	{
		return Task.FromResult(Require(RoomPrivileges.RoomClose) ?? Room.Close(Actor.Record));
	}
}