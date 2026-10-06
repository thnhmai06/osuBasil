using Basil.Application.Services.Users;
using Basil.Application.Storage.Multiplayer;
using Basil.Application.Storage.Sessions;
using Basil.Domain.Users;

namespace Basil.Application.Services.Multiplayer;

/// <summary>The authority rules of rooms, shared by the lobby and the room services.</summary>
internal static class RoomRules
{
	/// <summary>Gets a value that indicates whether a user is the creator or a referee of a room.</summary>
	/// <param name="room">The room to check.</param>
	/// <param name="user">The user to check.</param>
	/// <returns><see langword="true" /> if the user is the creator or a referee of this match.</returns>
	internal static bool IsManager(Room room, User user)
	{
		return room.Authority.IsManagedBy(user);
	}

	/// <summary>
	///     Gets a value that indicates whether a connection acts with the creator's authority: its user is the creator
	///     and has a permission in effect, or holds <see cref="Permissions.TournamentManageAnyRoom" />.
	/// </summary>
	/// <param name="room">The room to check.</param>
	/// <param name="by">The connection to check.</param>
	/// <param name="now">The moment to evaluate at.</param>
	internal static bool IsCreatorOrAnyRoomManager(Room room, Connection by, DateTimeOffset now)
	{
		return PermissionRules.Allows(by, Permissions.TournamentManageAnyRoom, now) ||
		       (room.Authority.Creator is not null && room.Authority.Creator.Equals(by.User) && HasAnyPermission(by, now));
	}

	/// <summary>
	///     Gets a value that indicates whether a connection may manage a room: its user is the creator or a referee and
	///     has a permission in effect, or holds <see cref="Permissions.TournamentManageAnyRoom" />.
	/// </summary>
	/// <param name="room">The room to check.</param>
	/// <param name="by">The connection to check.</param>
	/// <param name="now">The moment to evaluate at.</param>
	internal static bool CanManage(Room room, Connection by, DateTimeOffset now)
	{
		return PermissionRules.Allows(by, Permissions.TournamentManageAnyRoom, now) ||
		       (IsManager(room, by.User) && HasAnyPermission(by, now));
	}

	/// <summary>Gets a value that indicates whether a connection is the room's host or may manage it.</summary>
	/// <param name="room">The room to check.</param>
	/// <param name="by">The connection to check.</param>
	/// <param name="now">The moment to evaluate at.</param>
	internal static bool IsHostOrManager(Room room, Connection by, DateTimeOffset now)
	{
		return ReferenceEquals(by, room.Authority.Host) || CanManage(room, by, now);
	}

	/// <summary>A creator or referee whose permissions are all suspended cannot use their authority.</summary>
	private static bool HasAnyPermission(Connection by, DateTimeOffset now)
	{
		return PermissionRules.Effective(by.Session, now) != Permissions.None;
	}
}