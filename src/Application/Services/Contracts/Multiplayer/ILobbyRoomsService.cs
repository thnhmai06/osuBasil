using Basil.Application.Services.Contracts.Multiplayer.Events;
using Basil.Application.Storage.Contracts.Multiplayer.Rooms;
using Basil.Application.Storage.Contracts.Sessions;
using Basil.Domain.Multiplayer.Matches;
using Basil.Domain.Users;

namespace Basil.Application.Services.Contracts.Multiplayer;

/// <summary>Opens and closes rooms.</summary>
public interface ILobbyRoomsService
{
	/// <summary>Opens a room for a new match, created by the caller.</summary>
	/// <param name="by">
	///     The connection of the creator; it needs <see cref="Permissions.RoomCreate" />, and for a room that is
	///     not a tournament room it must be the creator's osu! client and need <see cref="Permissions.RoomJoin" />.
	/// </param>
	/// <param name="name">The room's name.</param>
	/// <param name="password">The room's password, or an empty string for none.</param>
	/// <param name="isTournament">Whether the room is a tournament room.</param>
	/// <param name="isPrivate">Whether the room's history is private.</param>
	/// <param name="settings">The room's initial settings, or <see langword="null" /> for the defaults.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     The opened room and Ok; otherwise Silenced when a needed permission is suspended, NotAuthorized when it is not
	///     granted, TooManyRooms, AlreadyInRoom or NoRoomId.
	/// </returns>
	/// <remarks>
	///     A room that is not a tournament room is opened in game: the creator's osu! client is seated as the first host,
	///     and the call fails with AlreadyInRoom when that client already plays in a room. When a tournament room opens,
	///     the creator's osu! client is seated as host if it is online, may join rooms and is not in any room. A creator
	///     can have at most 4 tournament rooms open unless they hold <see cref="Permissions.RoomCreateUnlimited" />.
	///     While room creation is locked (<see cref="Basil.Domain.Content.CreationLocks.Rooms" />), only a user with
	///     <see cref="Permissions.RoomManageAny" /> may open a room; anyone else gets NotAuthorized.
	///     A tournament room opened with nobody seated closes after 15 minutes unless someone joins. Seating the creator
	///     is reported by <see cref="LobbyRoomOpened" /> through its host.
	/// </remarks>
	/// <exception cref="ArgumentException">
	///     <paramref name="by" /> is not an osu! client for a room that is not a tournament room.
	/// </exception>
	Task<(Room? Room, RoomResult Result)> OpenAsync(
		Connection by,
		string name,
		string password,
		bool isTournament,
		bool isPrivate,
		MatchSettings? settings = null,
		CancellationToken cancellationToken = default);

	/// <summary>Closes a room.</summary>
	/// <param name="room">The room to close.</param>
	/// <param name="by">The creator, a referee or a user with <see cref="Permissions.RoomManageAny" />.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, or NotAuthorized when the caller may not close the room.</returns>
	/// <remarks>Closing a closed room returns Ok and does nothing.</remarks>
	Task<RoomResult> CloseAsync(Room room, Connection by, CancellationToken cancellationToken = default);
}