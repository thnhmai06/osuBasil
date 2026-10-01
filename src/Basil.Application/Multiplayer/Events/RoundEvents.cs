using Basil.Application.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Events;

/// <summary>Something happened during a room round.</summary>
public abstract record RoundEvent(Room Room, Round Round) : RoomEvent(Room);

/// <summary>A round started.</summary>
/// <param name="Room">The room.</param>
/// <param name="Round">The round.</param>
/// <param name="Players">The players taking part.</param>
public sealed record RoundStarted(Room Room, Round Round, IReadOnlyList<BanchoConnection> Players)
	: RoundEvent(Room, Round);

/// <summary>A round was aborted.</summary>
public sealed record RoundAborted(Room Room, Round Round) : RoundEvent(Room, Round);

/// <summary>A round ended because every player completed it or left.</summary>
/// <param name="Room">The room.</param>
/// <param name="Round">The round.</param>
/// <param name="Slot">
///     The slot of the player whose completion ended the round, or <see langword="null" /> when the last
///     remaining player left.
/// </param>
public sealed record RoundCompleted(Room Room, Round Round, int? Slot) : RoundEvent(Room, Round);

/// <summary>A player finished loading the beatmap.</summary>
/// <param name="Room">The room.</param>
/// <param name="Round">The round.</param>
/// <param name="Slot">The slot number, from 1 to 16.</param>
public sealed record PlayerLoaded(Room Room, Round Round, int Slot) : RoundEvent(Room, Round);

/// <summary>The last player finished loading, so every player has loaded.</summary>
/// <param name="Room">The room.</param>
/// <param name="Round">The round.</param>
/// <param name="Slot">
///     The slot of the player whose load completed the set, or <see langword="null" /> when a player who
///     had not loaded left.
/// </param>
public sealed record AllPlayersLoaded(Room Room, Round Round, int? Slot) : RoundEvent(Room, Round);

/// <summary>A player asked to skip the intro.</summary>
/// <param name="Room">The room.</param>
/// <param name="Round">The round.</param>
/// <param name="Slot">The slot number, from 1 to 16.</param>
public sealed record PlayerSkipped(Room Room, Round Round, int Slot) : RoundEvent(Room, Round);

/// <summary>The last player asked to skip the intro, so the intro is skipped.</summary>
/// <param name="Room">The room.</param>
/// <param name="Round">The round.</param>
/// <param name="Slot">
///     The slot of the player whose request completed the set, or <see langword="null" /> when a player who
///     had not asked left.
/// </param>
public sealed record AllPlayersSkipped(Room Room, Round Round, int? Slot) : RoundEvent(Room, Round);

/// <summary>A player failed.</summary>
/// <param name="Room">The room.</param>
/// <param name="Round">The round.</param>
/// <param name="Slot">The slot number, from 1 to 16.</param>
public sealed record PlayerFailed(Room Room, Round Round, int Slot) : RoundEvent(Room, Round);

/// <summary>A player completed the beatmap.</summary>
/// <param name="Room">The room.</param>
/// <param name="Round">The round.</param>
/// <param name="Slot">The slot number, from 1 to 16.</param>
public sealed record PlayerCompleted(Room Room, Round Round, int Slot) : RoundEvent(Room, Round);

/// <summary>A player's score for a round was stored.</summary>
/// <param name="Room">The room.</param>
/// <param name="Round">The round the score was played in.</param>
/// <param name="Player">The player who submitted the score.</param>
/// <param name="Score">The stored score.</param>
public sealed record ScoreSubmitted(Room Room, Round Round, User Player, Score Score) : RoundEvent(Room, Round);