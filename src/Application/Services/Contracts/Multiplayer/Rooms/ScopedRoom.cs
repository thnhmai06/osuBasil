using Basil.Application.Services.Contracts.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Services.Contracts.Multiplayer.Rooms;

/// <summary>A room scoped to a connection, with its permissions and role snapshot.</summary>
public sealed class ScopedRoom
{
	/// <summary>The room this scope is over.</summary>
	public Room Room { get; }

	/// <summary>The connection this scope is for.</summary>
	public Connection By { get; }

	/// <summary>The roles this connection holds in the room.</summary>
	public RoomRoles Roles { get; }

	/// <summary>The permissions this connection has in the room, after applying effective permissions.</summary>
	public RoomPermissions Rights { get; }

	/// <summary>The slots service for this scoped room.</summary>
	public ScopedRoomSlots Slots { get; }

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
		var rights = RoomPermissionsGroups.Of(Roles);

		// Apply effective permissions
		var now = room.Context.Clock.GetUtcNow();
		var effectivePermissions = by.Session.User.Value.Permissions.Effective(by.Session.Restrictions, now);
		var grantedPermissions = by.Session.User.Value.Permissions;

		// If effective permissions lack RoomJoin, drop role-derived rights
		if (!effectivePermissions.Allows(Permissions.RoomJoin))
			rights = RoomPermissions.None;
		// If effective permissions allow RoomManageAny, grant all rights
		else if (effectivePermissions.Allows(Permissions.RoomManageAny))
			rights = RoomPermissionsGroups.All;

		Rights = rights;
		Slots = new ScopedRoomSlots(this);
	}

	/// <summary>Checks if the scoped connection has the required permissions.</summary>
	/// <param name="needed">The permissions required.</param>
	/// <returns>Null if allowed, otherwise the reason for denial.</returns>
	internal RoomResult? Require(RoomPermissions needed)
	{
		if ((Rights & needed) == needed)
			return null;

		// Check if granted permissions allow RoomJoin but effective don't
		var granted = By.Session.User.Value.Permissions;
		var effective = By.Session.User.Value.Permissions.Effective(By.Session.Restrictions, Room.Context.Clock.GetUtcNow());

		if (granted.Allows(Permissions.RoomJoin) && !effective.Allows(Permissions.RoomJoin))
			return RoomResult.Silenced;

		return RoomResult.NotAuthorized;
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
		if (room.Authority.IsManagedBy(by.User) && !Equals(room.Authority.Creator, by.User))
			roles |= RoomRoles.Referee;

		// Creator: creator equals
		if (Equals(room.Authority.Creator, by.User))
			roles |= RoomRoles.Creator;

		return roles;
	}
}
