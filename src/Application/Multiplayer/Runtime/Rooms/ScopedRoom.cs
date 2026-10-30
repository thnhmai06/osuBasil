using Basil.Application.Common;
using Basil.Application.Common.Common;
using Basil.Application.Users.Connections;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Runtime.Rooms;

/// <summary>A room scoped to a connection, with its permissions and role snapshot.</summary>
/// <remarks>
///     The snapshot is taken once, so a scope is for one request or command: create it with
///     <see cref="Room.ScopeTo" />, use it, drop it. Keeping one past the request lets its rights outlive a change of
///     role or restriction.
/// </remarks>
public sealed class ScopedRoom : Scoped<RoomPermissions, Connection>
{
	/// <summary>The permissions the connection has globally, as they were when the scope was created.</summary>
	private readonly UserPermissions _global;

	/// <summary>Creates a scoped room for a connection.</summary>
	/// <param name="room">The room to scope.</param>
	/// <param name="by">The connection acting on the room.</param>
	internal ScopedRoom(Room room, Connection by) : this(room, by, Snapshot(room, by))
	{
	}

	private ScopedRoom(Room room, Connection by,
		(UserPermissions Global, RoomRoles Roles, RoomPermissions Rights) snapshot)
		: base(by, snapshot.Rights)
	{
		Room = room;
		_global = snapshot.Global;
		Roles = snapshot.Roles;
		ManagesAnyRoom = _global.Allows(UserPermissions.RoomManageAny);
		Slots = new ScopedRoomSlots(this);
		Rounds = new ScopedRoomRounds(this);
		Settings = new ScopedRoomSettings(this);
		Authority = new ScopedRoomAuthority(this);
		Members = new ScopedRoomMembers(this);
	}

	/// <summary>The room this scope is over.</summary>
	public Room Room { get; }

	/// <summary>The roles this connection holds in the room.</summary>
	public RoomRoles Roles { get; }

	/// <summary>The slots service for this scoped room.</summary>
	public ScopedRoomSlots Slots { get; }

	/// <summary>The rounds service for this scoped room.</summary>
	public ScopedRoomRounds Rounds { get; }

	/// <summary>The settings service for this scoped room.</summary>
	public ScopedRoomSettings Settings { get; }

	/// <summary>The authority service for this scoped room.</summary>
	public ScopedRoomAuthority Authority { get; }

	/// <summary>The membership operations for this scoped room.</summary>
	public ScopedRoomMembers Members { get; }

	/// <summary>Whether the connection may manage every room.</summary>
	internal bool ManagesAnyRoom { get; }

	private static (UserPermissions Global, RoomRoles Roles, RoomPermissions Rights) Snapshot(Room room, Connection by)
	{
		var global = by.EffectivePermissions(room.Context.Clock.GetUtcNow());
		var roles = ComputeRoles(room, by);

		// Managing every room acts with the creator's authority, joined or not; a restriction cannot suspend it,
		// because whoever manages an area keeps it in the effective permissions. Otherwise a user who may not join
		// rooms has no role-derived rights.
		var rights = global.Allows(UserPermissions.RoomManageAny)
			? RoomPermissionsGroups.All
			: global.Allows(UserPermissions.RoomJoin)
				? roles.GetPermissions()
				: RoomPermissions.None;

		return (global, roles, rights);
	}

	private static RoomRoles ComputeRoles(Room room, Connection by)
	{
		var roles = RoomRoles.None;

		// Player: seated BanchoConnection
		if (by is BanchoConnection bancho && room.Slots.Find(bancho) is not null)
			roles |= RoomRoles.Player;

		// Host: reference equals
		if (ReferenceEquals(by, room.Authority.Host))
			roles |= RoomRoles.Host;

		// Referee: managed by but not creator
		if (room.Authority.IsManagedBy(by.Record) && !Equals(room.Authority.Creator, by.Record))
			roles |= RoomRoles.Referee;

		// Creator: creator equals
		if (Equals(room.Authority.Creator, by.Record))
			roles |= RoomRoles.Creator;

		return roles;
	}

	/// <summary>Checks that the scoped connection holds the room rights an action needs.</summary>
	/// <param name="needed">The room rights required.</param>
	/// <returns>
	///     <see langword="null" /> when allowed; a denial naming <see cref="UserPermissions.RoomJoin" /> when the caller may
	///     not
	///     join rooms at all; otherwise <see cref="RoomOutcome.RoleDenied" />.
	/// </returns>
	internal new Result<RoomOutcome>? Require(RoomPermissions needed)
	{
		if (base.Require(needed) is null)
			return null;

		return !ManagesAnyRoom && RequireJoin() is { } cannotJoin ? cannotJoin : RoomOutcome.RoleDenied;
	}

	/// <summary>Checks whether the connection may join rooms.</summary>
	/// <returns><see langword="null" /> when allowed, otherwise a denial naming the missing permission.</returns>
	internal Result<RoomOutcome>? RequireJoin()
	{
		return _global.Allows(UserPermissions.RoomJoin)
			? null
			: Result<RoomOutcome>.Denied(UserPermissions.RoomJoin);
	}

	/// <summary>Closes the room, evicting its players, ending its round and forgetting it.</summary>
	/// <param name="cancellationToken">A token that cancels the wait for the room.</param>
	/// <returns>
	///     Ok, or a denial; a room that has closed already returns Ok and does nothing.
	/// </returns>
	/// <remarks>The caller needs <see cref="RoomPermissions.RoomClose" />, checked before the room is entered.</remarks>
	public Task<Result<RoomOutcome>> CloseAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult(Require(RoomPermissions.RoomClose) ?? Room.Close(By.Record));
	}
}