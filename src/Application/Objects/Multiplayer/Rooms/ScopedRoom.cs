using Basil.Application.Objects.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Domain.Permissions;

namespace Basil.Application.Objects.Multiplayer.Rooms;

/// <summary>A room scoped to a connection, with its permissions and role snapshot.</summary>
/// <remarks>
///     The snapshot is taken once, so a scope is for one request or command: create it with
///     <see cref="Room.ScopeTo" />, use it, drop it. Keeping one past the request lets its rights outlive a change of
///     role or restriction.
/// </remarks>
public sealed class ScopedRoom
{
	/// <summary>The room this scope is over.</summary>
	public Room Room { get; }

	/// <summary>The connection this scope is for.</summary>
	public Connection By { get; }

	/// <summary>The roles this connection holds in the room.</summary>
	public RoomRoles Roles { get; }

	/// <summary>The permissions this connection has in the room, after applying effective permissions.</summary>
	public RoomPermissions Permissions { get; }

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

	/// <summary>Creates a scoped room for a connection.</summary>
	/// <param name="room">The room to scope.</param>
	/// <param name="by">The connection acting on the room.</param>
	internal ScopedRoom(Room room, Connection by)
	{
		Room = room;
		By = by;

		// Snapshot roles
		Roles = ComputeRoles(room, by);

		// Compute rights from roles
		var rights = Roles.GetPermissions();

		// Apply effective permissions
		var now = room.Context.Clock.GetUtcNow();
		var effectivePermissions = by.Session.EffectivePermissions(now);
		ManagesAnyRoom = effectivePermissions.Allows(Domain.Permissions.Permissions.RoomManageAny);

		// Managing every room acts with the creator's authority, joined or not; a restriction cannot suspend it,
		// because whoever manages an area keeps it in the effective permissions.
		if (ManagesAnyRoom)
			rights = RoomPermissionsGroups.All;
		// Otherwise a user who may not join rooms has no role-derived rights.
		else if (!effectivePermissions.Allows(Domain.Permissions.Permissions.RoomJoin))
			rights = RoomPermissions.None;

		Permissions = rights;
		Slots = new ScopedRoomSlots(this);
		Rounds = new ScopedRoomRounds(this);
		Settings = new ScopedRoomSettings(this);
		Authority = new ScopedRoomAuthority(this);
		Members = new ScopedRoomMembers(this);

		return;

		static RoomRoles ComputeRoles(Room room, Connection by)
		{
			var roles = RoomRoles.None;

			// Player: seated BanchoConnection
			if (by is BanchoConnection bancho && room.Slots.Find(bancho) is not null)
				roles |= RoomRoles.Player;

			// Host: reference equals
			if (ReferenceEquals(by, room.Authority.Host))
				roles |= RoomRoles.Host;

			// Referee: managed by but not creator
			if (room.Authority.IsManagedBy(by.User) && !Equals(room.Authority.Creator, by.User))
				roles |= RoomRoles.Referee;

			// Creator: creator equals
			if (Equals(room.Authority.Creator, by.User))
				roles |= RoomRoles.Creator;

			return roles;
		}
	}

	/// <summary>Checks if the scoped connection has the required permissions.</summary>
	/// <param name="needed">The permissions required.</param>
	/// <returns>Null if allowed, otherwise the reason for denial.</returns>
	internal RoomResult? Require(RoomPermissions needed)
	{
		if ((Permissions & needed) == needed)
			return null;

		// Check if granted permissions allow RoomJoin but effective don't
		if (By.Session.Check(Domain.Permissions.Permissions.RoomJoin, Room.Context.Clock.GetUtcNow()) is Access
			    .Suspended)
			return RoomResult.Silenced;

		return RoomResult.NotAuthorized;
	}

	/// <summary>Checks whether the connection may join the room.</summary>
	/// <returns>Null when allowed, otherwise the reason for denial.</returns>
	internal RoomResult? RequireJoin()
	{
		return By.Session.Check(Domain.Permissions.Permissions.RoomJoin, Room.Context.Clock.GetUtcNow()) switch
		{
			Access.NotGranted => RoomResult.NotAuthorized,
			Access.Suspended => RoomResult.Silenced,
			_ => null
		};
	}

	/// <summary>Closes the room, evicting its players, ending its round, closing its channel and forgetting it.</summary>
	/// <param name="cancellationToken">A token that cancels the wait for the room.</param>
	/// <returns>
	///     Ok, NotAuthorized or Silenced; a room that has closed already returns Ok and does nothing.
	/// </returns>
	/// <remarks>The caller needs <see cref="RoomPermissions.RoomClose" />, checked before the room is entered.</remarks>
	public async Task<RoomResult> CloseAsync(CancellationToken cancellationToken = default)
	{
		if (Require(RoomPermissions.RoomClose) is { } denied) return denied;

		await using var scope = await Room.EnterAsync(cancellationToken);
		if (scope is null) return RoomResult.Ok;

		Room.Context.Lobby.Close(Room, By.User);
		return RoomResult.Ok;
	}
}