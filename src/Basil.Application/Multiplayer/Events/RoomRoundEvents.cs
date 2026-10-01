using Basil.Application.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Events;

/// <summary>Something happened during a room round.</summary>
public abstract record RoomRoundEvent(Room Room, Round Round) : RoomEvent(Room);

/// <summary>A round started.</summary>
/// <param name="Room">The room.</param>
/// <param name="Round">The round.</param>
/// <param name="Players">The players taking part.</param>
public sealed record RoomRoundStarted(Room Room, Round Round, IReadOnlyList<BanchoConnection> Players)
	: RoomRoundEvent(Room, Round);

/// <summary>A round was aborted.</summary>
public sealed record RoomRoundAborted(Room Room, Round Round) : RoomRoundEvent(Room, Round);

/// <summary>A round ended because every player completed it or left.</summary>
/// <param name="Room">The room.</param>
/// <param name="Round">The round.</param>
/// <param name="Slot">
///     The slot of the player whose completion ended the round, or <see langword="null" /> when the last
///     remaining player left.
/// </param>
public sealed record RoomRoundCompleted(Room Room, Round Round, int? Slot) : RoomRoundEvent(Room, Round);

/// <summary>A player finished loading the beatmap.</summary>
/// <param name="Room">The room.</param>
/// <param name="Round">The round.</param>
/// <param name="Slot">The slot number, from 1 to 16.</param>
public sealed record RoomRoundPlayerLoaded(Room Room, Round Round, int Slot) : RoomRoundEvent(Room, Round);

/// <summary>The last player finished loading, so every player has loaded.</summary>
/// <param name="Room">The room.</param>
/// <param name="Round">The round.</param>
/// <param name="Slot">
///     The slot of the player whose load completed the set, or <see langword="null" /> when a player who
///     had not loaded left.
/// </param>
public sealed record RoomRoundAllLoaded(Room Room, Round Round, int? Slot) : RoomRoundEvent(Room, Round);

/// <summary>A player asked to skip the intro.</summary>
/// <param name="Room">The room.</param>
/// <param name="Round">The round.</param>
/// <param name="Slot">The slot number, from 1 to 16.</param>
public sealed record RoomRoundPlayerSkipped(Room Room, Round Round, int Slot) : RoomRoundEvent(Room, Round);

/// <summary>The last player asked to skip the intro, so the intro is skipped.</summary>
/// <param name="Room">The room.</param>
/// <param name="Round">The round.</param>
/// <param name="Slot">
///     The slot of the player whose request completed the set, or <see langword="null" /> when a player who
///     had not asked left.
/// </param>
public sealed record RoomRoundAllSkipped(Room Room, Round Round, int? Slot) : RoomRoundEvent(Room, Round);

/// <summary>A player failed.</summary>
/// <param name="Room">The room.</param>
/// <param name="Round">The round.</param>
/// <param name="Slot">The slot number, from 1 to 16.</param>
public sealed record RoomRoundPlayerFailed(Room Room, Round Round, int Slot) : RoomRoundEvent(Room, Round);

/// <summary>A player completed the beatmap.</summary>
/// <param name="Room">The room.</param>
/// <param name="Round">The round.</param>
/// <param name="Slot">The slot number, from 1 to 16.</param>
public sealed record RoomRoundPlayerCompleted(Room Room, Round Round, int Slot) : RoomRoundEvent(Room, Round);

/// <summary>A player's score for a round was stored.</summary>
/// <param name="Room">The room.</param>
/// <param name="Round">The round the score was played in.</param>
/// <param name="Player">The player who submitted the score.</param>
/// <param name="Score">The stored score.</param>
public sealed record RoomRoundScoreSubmitted(Room Room, Round Round, User Player, Score Score) : RoomRoundEvent(Room, Round);