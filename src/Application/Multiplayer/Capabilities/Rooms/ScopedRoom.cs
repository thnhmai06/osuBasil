using Basil.Application.Common;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Capabilities.Rooms;

/// <summary>A room scoped to a connection, with its permissions and role snapshot.</summary>
/// <remarks>
///     The snapshot is taken once, so a scope is for one request or command: create it with
///     <see cref="Objects.Rooms.Room.ScopeTo" />, use it, drop it. Keeping one past the request lets its rights outlive a
///     change of
///     role or privilege.
/// </remarks>
public readonly struct ScopedRoom
{
	/// <summary>The privileges the connection holds, as they were when the scope was created.</summary>
	private readonly UserPrivileges _global;

	/// <summary>Gets the connection this scope is for.</summary>
	public Connection Actor { get; }

	/// <summary>Gets the room rights the connection held when the scope was created.</summary>
	public RoomPermissions Rights { get; }

	/// <summary>Creates a scoped room for a connection.</summary>
	/// <param name="room">The room to scope.</param>
	/// <param name="actor">The connection acting on the room.</param>
	internal ScopedRoom(Room room, Connection actor) : this(room, actor, Snapshot(room, actor))
	{
	}

	private ScopedRoom(Room room, Connection actor,
		(UserPrivileges Global, RoomRoles Roles, RoomPermissions Rights) snapshot)
	{
		Actor = actor;
		Rights = snapshot.Rights;
		Room = room;
		_global = snapshot.Global;
		Roles = snapshot.Roles;
		ManagesAnyRoom = _global.Allows(UserPrivileges.Tournament);
	}

	/// <summary>The room this scope is over.</summary>
	public Room Room { get; }

	/// <summary>The roles this connection holds in the room.</summary>
	public RoomRoles Roles { get; }

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

	private static (UserPrivileges Global, RoomRoles Roles, RoomPermissions Rights) Snapshot(Room room,
		Connection actor)
	{
		var global = actor.EffectivePrivileges(room.Context.Clock.GetUtcNow());
		var roles = ComputeRoles(room, actor);

		// Managing every room acts with the creator's authority, joined or not. Otherwise a user without the player
		// privilege has no role-derived rights.
		var rights = global.Allows(UserPrivileges.Tournament)
			? RoomPermissionsGroups.All
			: global.Allows(UserPrivileges.Player)
				? roles.GetPermissions()
				: RoomPermissions.None;

		return (global, roles, rights);
	}

	private static RoomRoles ComputeRoles(Room room, Connection actor)
	{
		var roles = RoomRoles.None;

		// Player: seated StableConnection
		if (actor is StableConnection stable && room.Slots.Find(stable) is not null)
			roles |= RoomRoles.Player;

		// Host: reference equals
		if (ReferenceEquals(actor, room.Authority.Host))
			roles |= RoomRoles.Host;

		// Referee: managed by but not creator
		if (room.Authority.IsManagedBy(actor.Record) && !Equals(room.Authority.Creator, actor.Record))
			roles |= RoomRoles.Referee;

		// Creator: creator equals
		if (Equals(room.Authority.Creator, actor.Record))
			roles |= RoomRoles.Creator;

		return roles;
	}

	/// <summary>Checks that the scoped connection holds the room rights an action needs.</summary>
	/// <param name="needed">The room rights required.</param>
	/// <returns>
	///     <see langword="null" /> when allowed; a denial naming <see cref="UserPrivileges.Player" /> when the caller may
	///     not
	///     join rooms at all; otherwise <see cref="RoomOutcome.RoleDenied" />.
	/// </returns>
	internal Result<RoomOutcome>? Require(RoomPermissions needed)
	{
		if (ScopedRights.Require(Rights, needed) is null)
			return null;

		return !ManagesAnyRoom && RequireJoin() is { } cannotJoin ? cannotJoin : RoomOutcome.RoleDenied;
	}

	/// <summary>
	///     Gets whether the connection takes part in the room: it is seated or observing, it manages the room while
	///     allowed to join rooms, or it manages every room.
	/// </summary>
	public bool Involves => Room.Involves(Actor, _global);

	/// <summary>Checks whether the connection may join rooms.</summary>
	/// <returns><see langword="null" /> when allowed, otherwise a denial naming the missing privilege.</returns>
	internal Result<RoomOutcome>? RequireJoin()
	{
		return _global.Allows(UserPrivileges.Player)
			? null
			: Result<RoomOutcome>.Denied(UserPrivileges.Player);
	}

	/// <summary>Closes the room, evicting its players, ending its round and forgetting it.</summary>
	/// <returns>
	///     Ok, or a denial; a room that has closed already returns Ok and does nothing.
	/// </returns>
	/// <remarks>The caller needs <see cref="RoomPermissions.RoomClose" />, checked before the room is entered.</remarks>
	public Task<Result<RoomOutcome>> CloseAsync()
	{
		return Task.FromResult(Require(RoomPermissions.RoomClose) ?? Room.Close(Actor.Record));
	}
}