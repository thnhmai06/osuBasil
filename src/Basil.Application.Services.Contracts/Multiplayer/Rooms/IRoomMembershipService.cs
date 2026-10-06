using Basil.Domain.Users;
using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Sessions;

namespace Basil.Application.Services.Contracts.Multiplayer.Rooms;

/// <summary>Joining, leaving, seating, removing and inviting players, and tourney observers.</summary>
public interface IRoomMembershipService
{
	/// <summary>Seats a player in the first open slot.</summary>
	/// <param name="room">The room to join.</param>
	/// <param name="by">The joining player's game client.</param>
	/// <param name="password">The password the player supplied.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     Ok, AlreadySeated, Banned, Silenced, NotAuthorized, InAnotherRoom, IsObserver, WrongPassword, Full or
	///     RoomClosed when the room has closed.
	/// </returns>
	/// <remarks>
	///     If the same user is seated through a connection that has closed, that connection leaves first.
	///     A user with <see cref="Permissions.TournamentManageAnyRoom" /> needs no password. The player also joins the
	///     room's chat channel.
	/// </remarks>
	Task<RoomResult> JoinAsync(Room room, BanchoConnection by, string password,
		CancellationToken cancellationToken = default);

	/// <summary>Seats an online player in a room on behalf of its managers.</summary>
	/// <param name="room">The room to seat the player in.</param>
	/// <param name="by">The creator, a referee or a user with <see cref="Permissions.TournamentManageAnyRoom" />.</param>
	/// <param name="player">The player's osu! client.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     Ok, NotAuthorized when the caller does not manage the room or the player may not play, TargetOffline,
	///     AlreadySeated, Banned, Silenced, InAnotherRoom when the player sits in a room the caller does not manage,
	///     IsObserver, Full or RoomClosed.
	/// </returns>
	/// <remarks>
	///     The room's password is not asked for; a ban and the player's permissions still apply. A player in another
	///     room is moved only when the caller manages that room too. The checks run again once the player has left their
	///     previous room; if the room filled up meanwhile, the player is left without a seat.
	/// </remarks>
	Task<RoomResult> SeatAsync(Room room, Connection by, BanchoConnection player,
		CancellationToken cancellationToken = default);

	/// <summary>Removes a player from the room.</summary>
	/// <param name="room">The room to leave.</param>
	/// <param name="by">The leaving player's game client.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotInRoom or RoomClosed when the room has closed; leaving again returns NotInRoom and does nothing.</returns>
	/// <remarks>
	///     If the host leaves, the next seated player by slot order becomes host. A room left empty is closed by the lobby.
	/// </remarks>
	Task<RoomResult> LeaveAsync(Room room, BanchoConnection by, CancellationToken cancellationToken = default);

	/// <summary>Removes another player from the room.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The host or a manager.</param>
	/// <param name="player">The user to remove.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, IsManager, NotInRoom or RoomClosed when the room has closed.</returns>
	/// <remarks>The creator and referees cannot be kicked.</remarks>
	Task<RoomResult> KickAsync(Room room, Connection by, User player, CancellationToken cancellationToken = default);

	/// <summary>Bans a user from the room, removing them if seated.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">A manager.</param>
	/// <param name="player">The user to ban.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     Ok, NotAuthorized, IsManager or RoomClosed when the room has closed; banning a banned user again returns Ok
	///     and does nothing.
	/// </returns>
	Task<RoomResult> BanAsync(Room room, Connection by, User player, CancellationToken cancellationToken = default);

	/// <summary>Lifts a user's ban.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">A manager.</param>
	/// <param name="player">The banned user.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, NotBanned or RoomClosed when the room has closed.</returns>
	Task<RoomResult> UnbanAsync(Room room, Connection by, User player, CancellationToken cancellationToken = default);

	/// <summary>Invites an online user to the room.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">A seated player or a manager.</param>
	/// <param name="target">The online session of the invited user.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, TargetOffline, AlreadyInRoom or RoomClosed when the room has closed.</returns>
	/// <remarks>The server's bot cannot be invited.</remarks>
	Task<RoomResult> InviteAsync(Room room, Connection by, UserSession target,
		CancellationToken cancellationToken = default);

	/// <summary>Starts observing the room from an osu!tourney client.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The osu!tourney client.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, IsPlayer or RoomClosed when the room has closed; observing again returns Ok and does nothing.</returns>
	/// <remarks>The observer also joins the room's chat channel.</remarks>
	Task<RoomResult> ObserverJoinAsync(Room room, TourneyConnection by, CancellationToken cancellationToken = default);

	/// <summary>Stops observing the room.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The osu!tourney client.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotObserver or RoomClosed when the room has closed.</returns>
	Task<RoomResult> ObserverLeaveAsync(Room room, TourneyConnection by, CancellationToken cancellationToken = default);

	/// <summary>Removes a connection from every room it plays in or observes.</summary>
	/// <param name="connection">The connection to release.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>Called when the connection closes or its user is silenced; calling it again does nothing.</remarks>
	Task ReleaseAsync(Connection connection, CancellationToken cancellationToken = default);
}
