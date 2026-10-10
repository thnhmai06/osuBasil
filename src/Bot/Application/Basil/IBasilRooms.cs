using Basil.Domain.Mechanics;
using Basil.Domain.Users;

namespace Basil.Bot.Application.Basil;

/// <summary>Reads multiplayer rooms and runs room operations on behalf of a user.</summary>
/// <remarks>
///     Every operation runs with the authority of <c>actor</c>, the user who asked the bot for it; the server checks
///     that authority and answers with a <see cref="RoomOutcome" />.
/// </remarks>
public interface IBasilRooms
{
	/// <summary>Gets an open room as the bot sees it, for the bot's own announcements.</summary>
	/// <param name="roomId">The room id.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The room, or <see langword="null" /> when no open room has that id.</returns>
	Task<RoomState?> GetAsync(int roomId, CancellationToken cancellationToken = default);

	/// <summary>Gets an open room as a user may see it.</summary>
	/// <param name="viewer">The user asking.</param>
	/// <param name="roomId">The room id.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     The room, or <see langword="null" /> when no open room has that id or the user may not see it (the same rule
	///     as reading the room's chat channel).
	/// </returns>
	Task<RoomState?> GetForAsync(User viewer, int roomId, CancellationToken cancellationToken = default);

	/// <summary>Gets the open room a user's osu! client is seated in.</summary>
	/// <param name="player">The user.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The room, or <see langword="null" /> when the user is not seated anywhere.</returns>
	Task<RoomState?> FindByPlayerAsync(User player, CancellationToken cancellationToken = default);

	/// <summary>Opens a tournament room with the actor as its creator.</summary>
	/// <param name="actor">The user opening the room.</param>
	/// <param name="name">The room name.</param>
	/// <param name="isPrivate">Whether the room's match history is private.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The new room, or <see langword="null" /> with the reason it was not opened.</returns>
	Task<(RoomState? Room, RoomOutcome Outcome)> OpenAsync(User actor, string name, bool isPrivate,
		CancellationToken cancellationToken = default);

	/// <summary>Seats the actor's osu! client in a room.</summary>
	/// <param name="actor">The user joining.</param>
	/// <param name="roomId">The room id.</param>
	/// <param name="password">The room password, empty when none.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome.</returns>
	Task<RoomOutcome> JoinAsync(User actor, int roomId, string password, CancellationToken cancellationToken = default);

	/// <summary>Closes a room.</summary>
	Task<RoomOutcome> CloseAsync(User actor, int roomId, CancellationToken cancellationToken = default);

	/// <summary>Changes a room's settings; only the fields set in <paramref name="change" /> change.</summary>
	Task<RoomOutcome> ConfigureAsync(User actor, int roomId, RoomChange change,
		CancellationToken cancellationToken = default);

	/// <summary>Locks or unlocks a room's slots against players moving themselves.</summary>
	Task<RoomOutcome> SetLockedAsync(User actor, int roomId, bool locked,
		CancellationToken cancellationToken = default);

	/// <summary>Moves a seated player to another slot (numbered 1 to 16).</summary>
	Task<RoomOutcome> MoveAsync(User actor, int roomId, User player, int slot,
		CancellationToken cancellationToken = default);

	/// <summary>Makes a seated player the host, or clears the host when <paramref name="host" /> is <see langword="null" />.</summary>
	Task<RoomOutcome> SetHostAsync(User actor, int roomId, User? host, CancellationToken cancellationToken = default);

	/// <summary>Invites an online user to a room.</summary>
	Task<RoomOutcome> InviteAsync(User actor, int roomId, User target, CancellationToken cancellationToken = default);

	/// <summary>Grants a user referee authority in a room.</summary>
	Task<RoomOutcome> AddRefereeAsync(User actor, int roomId, User referee,
		CancellationToken cancellationToken = default);

	/// <summary>Revokes a user's referee authority in a room.</summary>
	Task<RoomOutcome> RemoveRefereeAsync(User actor, int roomId, User referee,
		CancellationToken cancellationToken = default);

	/// <summary>Puts a seated player on a team.</summary>
	Task<RoomOutcome> SetTeamAsync(User actor, int roomId, User player, GameTeam team,
		CancellationToken cancellationToken = default);

	/// <summary>Starts the round now.</summary>
	Task<RoomOutcome> StartAsync(User actor, int roomId, CancellationToken cancellationToken = default);

	/// <summary>Starts a countdown, replacing any running one.</summary>
	/// <param name="actor">The user starting it.</param>
	/// <param name="roomId">The room id.</param>
	/// <param name="length">How long it runs.</param>
	/// <param name="startsRound">Whether the round starts when it ends.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome.</returns>
	Task<RoomOutcome> StartCountdownAsync(User actor, int roomId, TimeSpan length, bool startsRound,
		CancellationToken cancellationToken = default);

	/// <summary>Cancels the running countdown.</summary>
	Task<RoomOutcome> CancelCountdownAsync(User actor, int roomId, CancellationToken cancellationToken = default);

	/// <summary>Aborts the round in progress.</summary>
	Task<RoomOutcome> AbortAsync(User actor, int roomId, CancellationToken cancellationToken = default);

	/// <summary>Removes a seated player from a room.</summary>
	Task<RoomOutcome> KickAsync(User actor, int roomId, User player, CancellationToken cancellationToken = default);

	/// <summary>Bans a user from a room, removing them if seated.</summary>
	Task<RoomOutcome> BanAsync(User actor, int roomId, User player, CancellationToken cancellationToken = default);

	/// <summary>Lifts a user's ban from a room.</summary>
	Task<RoomOutcome> UnbanAsync(User actor, int roomId, User player, CancellationToken cancellationToken = default);
}

/// <summary>A change to a room's settings; <see langword="null" /> fields stay as they are.</summary>
/// <param name="Name">The new room name.</param>
/// <param name="BeatmapId">The id of the new beatmap.</param>
/// <param name="Mode">The new game mode.</param>
/// <param name="Mods">The new room mods.</param>
/// <param name="Freemods">Whether players choose their own mods.</param>
/// <param name="TeamType">The new team type.</param>
/// <param name="WinCondition">The new win condition.</param>
/// <param name="Password">The new password; empty removes it.</param>
/// <param name="Size">The number of usable slots, 1 to 16.</param>
/// <param name="IsPrivate">Whether the room's match history is private.</param>
public sealed record RoomChange(
	string? Name = null,
	int? BeatmapId = null,
	GameMode? Mode = null,
	GameMods? Mods = null,
	bool? Freemods = null,
	GameTeamType? TeamType = null,
	GameWinCondition? WinCondition = null,
	string? Password = null,
	int? Size = null,
	bool? IsPrivate = null);