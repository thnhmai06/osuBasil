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
	/// <returns><see langword="true" /> if the user is the creator or a referee of this match; otherwise, <see langword="false" />.</returns>
	internal static bool IsManager(Room room, User user)
	{
		return room.Referees.Contains(user) || (room.Creator is not null && room.Creator.Equals(user));
	}

	/// <summary>Gets a value that indicates whether a connection may manage a room: BasilBot's connection, which acts for the server, or the creator or a referee.</summary>
	/// <param name="room">The room to check.</param>
	/// <param name="by">The connection to check.</param>
	/// <returns><see langword="true" /> if the connection may manage the room; otherwise, <see langword="false" />.</returns>
	internal static bool CanManage(Room room, Connection by) => by is BotConnection || IsManager(room, by.User);

	/// <summary>Gets a value that indicates whether a connection is the room's host or may manage it.</summary>
	/// <param name="room">The room to check.</param>
	/// <param name="by">The connection to check.</param>
	/// <returns><see langword="true" /> if the connection is the host or may manage the room; otherwise, <see langword="false" />.</returns>
	internal static bool IsHostOrManager(Room room, Connection by) => ReferenceEquals(by, room.Host) || CanManage(room, by);
}