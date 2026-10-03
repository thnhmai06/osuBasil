using Basil.Application.Events;
using Basil.Application.Multiplayer;
using Basil.Application.Multiplayer.Events;
using Basil.Application.Sessions;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Application.Contracts.Multiplayer;

/// <summary>Changes rooms: membership, authority, settings, slots, rounds and countdowns.</summary>
public interface IRoomService : IEventPublisher<RoomEvent>
{
	/// <summary>Seats a player in the first open slot.</summary>
	/// <param name="room">The room to join.</param>
	/// <param name="by">The joining player's game client.</param>
	/// <param name="password">The password the player supplied.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, AlreadySeated, Banned, Silenced, NotAuthorized, InAnotherRoom, IsObserver, WrongPassword, Full or RoomClosed when the room has closed.</returns>
	/// <remarks>
	///     If the same user is seated through a connection that has closed, that connection leaves first.
	///     Moderators need no password. The player also joins the room's chat channel.
	/// </remarks>
	Task<RoomResult> JoinAsync(Room room, BanchoConnection by, string password, CancellationToken cancellationToken = default);

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
	/// <returns>Ok, NotAuthorized, IsManager or RoomClosed when the room has closed; banning a banned user again returns Ok and does nothing.</returns>
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
	Task<RoomResult> InviteAsync(Room room, Connection by, UserSession target, CancellationToken cancellationToken = default);

	/// <summary>Makes a user a referee.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The creator or BasilBot.</param>
	/// <param name="user">The user to make referee.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, IsCreator, AlreadyReferee, TooManyReferees or RoomClosed when the room has closed.</returns>
	Task<RoomResult> AddRefereeAsync(Room room, Connection by, User user, CancellationToken cancellationToken = default);

	/// <summary>Removes a user from the referees.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The creator or BasilBot.</param>
	/// <param name="user">The referee to remove.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, NotReferee or RoomClosed when the room has closed.</returns>
	Task<RoomResult> RemoveRefereeAsync(Room room, Connection by, User user, CancellationToken cancellationToken = default);

	/// <summary>Gives host to a seated player, or clears it.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The host or a manager.</param>
	/// <param name="host">The seated player to make host, or <see langword="null" /> to clear the host.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, NotInRoom or RoomClosed when the room has closed.</returns>
	/// <remarks>Giving host to the current host does nothing.</remarks>
	Task<RoomResult> SetHostAsync(Room room, Connection by, BanchoConnection? host, CancellationToken cancellationToken = default);

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

	/// <summary>Changes the room's settings in one step.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The host or a manager.</param>
	/// <param name="change">The settings to change.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, InProgress, InvalidSettings, InvalidMods or RoomClosed when the room has closed.</returns>
	/// <remarks>
	///     Nothing changes unless every field is valid. Selecting or clearing the beatmap sets ready players back to not ready.
	///     Turning freemod on moves the room's mods that are not speed-changing onto each player; turning it off gives the
	///     room the host's mods. Changing the team type reassigns teams. Changing the mode drops mods, the room's and the
	///     players', that the new mode does not allow. Under freemod, a seated caller's mods that are not speed-changing become
	///     that caller's own mods. A change with no field set does nothing.
	/// </remarks>
	Task<RoomResult> ConfigureAsync(Room room, Connection by, RoomSettingsChange change, CancellationToken cancellationToken = default);

	/// <summary>Moves the caller to another open slot.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The caller's game client.</param>
	/// <param name="index">The slot number, from 1 to 16.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotInRoom, RoomLocked, InProgress, SlotNotOpen or RoomClosed when the room has closed.</returns>
	/// <remarks>Moving to the caller's own slot returns Ok and does nothing.</remarks>
	Task<RoomResult> ChangeSlotAsync(Room room, BanchoConnection by, int index, CancellationToken cancellationToken = default);

	/// <summary>Moves a player to an empty, unlocked slot.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The caller's connection.</param>
	/// <param name="player">The user to act on.</param>
	/// <param name="index">The slot number, from 1 to 16.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, NotInRoom, SlotNotOpen or RoomClosed when the room has closed.</returns>
	/// <remarks>This is a referee operation, so it is allowed while the room is locked.</remarks>
	Task<RoomResult> MoveAsync(Room room, Connection by, User player, int index, CancellationToken cancellationToken = default);

	/// <summary>Locks or unlocks a slot; locking an occupied slot removes its player.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The caller's connection.</param>
	/// <param name="index">The slot number, from 1 to 16.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, SlotNotOpen, OwnSlot or RoomClosed when the room has closed.</returns>
	/// <remarks>A player cannot lock the slot they occupy.</remarks>
	Task<RoomResult> ToggleSlotLockAsync(Room room, Connection by, int index, CancellationToken cancellationToken = default);

	/// <summary>Locks or unlocks the room, which stops players from changing slot or team.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The caller's connection.</param>
	/// <param name="locked">The lock state to set.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized or RoomClosed when the room has closed; setting the current state again returns Ok and does nothing.</returns>
	Task<RoomResult> SetLockedAsync(Room room, Connection by, bool locked, CancellationToken cancellationToken = default);

	/// <summary>Marks the caller ready or not ready.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The caller's game client.</param>
	/// <param name="ready">Whether the caller is ready to play.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotInRoom, InProgress or RoomClosed when the room has closed.</returns>
	Task<RoomResult> SetReadyAsync(Room room, BanchoConnection by, bool ready, CancellationToken cancellationToken = default);

	/// <summary>Reports whether the caller has the selected beatmap.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The caller's game client.</param>
	/// <param name="has">Whether the caller has the selected beatmap.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotInRoom or RoomClosed when the room has closed.</returns>
	/// <remarks>
	///     The report is ignored while the caller is playing; having the map changes nothing unless the caller had reported not having it.
	/// </remarks>
	Task<RoomResult> SetHasMapAsync(Room room, BanchoConnection by, bool has, CancellationToken cancellationToken = default);

	/// <summary>Switches the caller to the other team.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The caller's game client.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotInRoom, NoTeams, RoomLocked, InProgress or RoomClosed when the room has closed.</returns>
	Task<RoomResult> ToggleTeamAsync(Room room, BanchoConnection by, CancellationToken cancellationToken = default);

	/// <summary>Puts a player on a team.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The caller's connection.</param>
	/// <param name="player">The user to act on.</param>
	/// <param name="team">The team to assign.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, NoTeams, NotInRoom or RoomClosed when the room has closed.</returns>
	Task<RoomResult> SetTeamAsync(Room room, Connection by, User player, GameTeam team, CancellationToken cancellationToken = default);

	/// <summary>Chooses the caller's own mods while freemod is on.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The caller's game client.</param>
	/// <param name="mods">The mods to select.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotInRoom, InProgress, NotFreemod, SpeedModNotAllowed, InvalidMods or RoomClosed when the room has closed.</returns>
	Task<RoomResult> SetPlayerModsAsync(Room room, BanchoConnection by, GameMods mods, CancellationToken cancellationToken = default);

	/// <summary>Starts the next round; players who have the beatmap start playing.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The host or a manager.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, InProgress, NoBeatmap or RoomClosed when the room has closed.</returns>
	/// <remarks>A running countdown is cancelled without a separate event.</remarks>
	Task<RoomResult> StartAsync(Room room, Connection by, CancellationToken cancellationToken = default);

	/// <summary>Aborts the round in progress; players go back to not ready.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">A manager.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, NotInProgress or RoomClosed when the room has closed.</returns>
	Task<RoomResult> AbortAsync(Room room, Connection by, CancellationToken cancellationToken = default);

	/// <summary>Reports that the caller finished loading the beatmap.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The caller's game client.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotPlaying or RoomClosed when the room has closed; reporting again returns Ok and does nothing.</returns>
	/// <remarks>
	///     The last player to load emits <see cref="RoomRoundAllLoaded" /> instead of <see cref="RoomRoundPlayerLoaded" />.
	/// </remarks>
	Task<RoomResult> MarkLoadedAsync(Room room, BanchoConnection by, CancellationToken cancellationToken = default);

	/// <summary>Reports that the caller wants to skip the intro.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The caller's game client.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotPlaying or RoomClosed when the room has closed; asking again returns Ok and does nothing.</returns>
	/// <remarks>
	///     The last player to ask emits <see cref="RoomRoundAllSkipped" /> instead of <see cref="RoomRoundPlayerSkipped" />.
	/// </remarks>
	Task<RoomResult> SkipAsync(Room room, BanchoConnection by, CancellationToken cancellationToken = default);

	/// <summary>Reports that the caller failed; the player keeps playing until completion.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The caller's game client.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotPlaying or RoomClosed when the room has closed.</returns>
	Task<RoomResult> FailAsync(Room room, BanchoConnection by, CancellationToken cancellationToken = default);

	/// <summary>Reports that the caller completed the beatmap.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The caller's game client.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotPlaying or RoomClosed when the room has closed.</returns>
	/// <remarks>
	///     When the last player completes, the round ends and <see cref="RoomRoundCompleted" /> is emitted instead of <see cref="RoomRoundPlayerCompleted" />.
	/// </remarks>
	Task<RoomResult> CompleteAsync(Room room, BanchoConnection by, CancellationToken cancellationToken = default);

	/// <summary>Starts a countdown, replacing any running one.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">A manager.</param>
	/// <param name="length">The countdown length, more than zero and at most one hour.</param>
	/// <param name="startsRound">Whether the round starts when the countdown ends.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, OutOfRange, InProgress, NoBeatmap or RoomClosed when the room has closed.</returns>
	/// <remarks>The countdown is announced at 60, 30, 10 and 5 seconds left, each only when shorter than its length.</remarks>
	Task<RoomResult> StartCountdownAsync(Room room, Connection by, TimeSpan length, bool startsRound, CancellationToken cancellationToken = default);

	/// <summary>Cancels the running countdown.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">A manager.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, NoCountdown or RoomClosed when the room has closed.</returns>
	Task<RoomResult> CancelCountdownAsync(Room room, Connection by, CancellationToken cancellationToken = default);

	/// <summary>Records a stored score of the room's latest round.</summary>
	/// <param name="room">The room.</param>
	/// <param name="player">The player who submitted the score.</param>
	/// <param name="score">The stored score.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, RoundMismatch or RoomClosed when the room has closed.</returns>
	Task<RoomResult> RecordScoreAsync(Room room, User player, Score score, CancellationToken cancellationToken = default);

	/// <summary>Removes a connection from every room it plays in or observes and from the lobby watchers.</summary>
	/// <param name="connection">The connection to release.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>Called when the connection closes or its user is silenced; calling it again does nothing.</remarks>
	Task ReleaseAsync(Connection connection, CancellationToken cancellationToken = default);

	/// <summary>Gets a value that indicates whether a user is the creator or a referee of a room.</summary>
	/// <param name="room">The room to check.</param>
	/// <param name="user">The user to check.</param>
	/// <returns><see langword="true" /> if the user is the creator or a referee of this match; otherwise, <see langword="false" />.</returns>
	bool IsManager(Room room, User user);
}