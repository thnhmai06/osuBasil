using Basil.Application.Events;
using Basil.Application.Multiplayer;
using Basil.Application.Multiplayer.Events;
using Basil.Application.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Contracts.Multiplayer;

/// <summary>Opens and closes rooms and tracks who watches the multiplayer lobby.</summary>
public interface ILobbyService : IEventPublisher<LobbyEvent>
{
	/// <summary>Opens a room for a new match.</summary>
	/// <param name="creator">The user creating the room, or <see langword="null" /> for an unattended room.</param>
	/// <param name="creatorConnection">The creator's game client, or <see langword="null" /> when they should not be seated.</param>
	/// <param name="name">The room's name.</param>
	/// <param name="password">The room's password, or an empty string for none.</param>
	/// <param name="isTournament">Whether the room is a tournament room.</param>
	/// <param name="isPrivate">Whether the room's history is private.</param>
	/// <param name="settings">The room's initial settings, or <see langword="null" /> for the defaults.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The opened room and the result of the operation.</returns>
	/// <remarks>
	///     A creator who is silenced or restricted cannot open a room, and a creator can have at most
	///     4 tournament rooms open. When <paramref name="creatorConnection" />
	///     is given and not seated in another room, it is seated as the first host. A room that is not a
	///     tournament room is opened in game: it needs the creator's game client and fails with AlreadyInRoom
	///     when that client already plays in a room. A tournament room opened with nobody seated closes after
	///     15 minutes unless someone joins. The server's bot joins the room's
	///     chat channel. Seating the creator is reported by <see cref="LobbyRoomOpened" /> through its host.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	///     <paramref name="creatorConnection" /> is <see langword="null" /> for a room that is not a tournament room.
	/// </exception>
	Task<(Room? Room, RoomResult Result)> OpenAsync(
		User? creator,
		BanchoConnection? creatorConnection,
		string name,
		string password,
		bool isTournament,
		bool isPrivate,
		MatchSettings? settings = null,
		CancellationToken cancellationToken = default);

	/// <summary>Closes a room.</summary>
	/// <param name="room">The room to close.</param>
	/// <param name="by">A connection whose user is the creator or a referee, or BasilBot's connection.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, or NotAuthorized when the caller may not close the room.</returns>
	/// <remarks>Only the creator, a referee or BasilBot can close a room. Closing a closed room returns Ok and does nothing.</remarks>
	Task<RoomResult> CloseAsync(Room room, Connection by, CancellationToken cancellationToken = default);

	/// <summary>Starts watching the multiplayer lobby.</summary>
	/// <param name="by">The osu! client that started watching.</param>
	void Watch(BanchoConnection by);

	/// <summary>Stops watching the multiplayer lobby; doing it again does nothing.</summary>
	/// <param name="by">The osu! client that stopped watching.</param>
	void Unwatch(BanchoConnection by);
}