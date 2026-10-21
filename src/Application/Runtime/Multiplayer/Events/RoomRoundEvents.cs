using Basil.Application.Runtime.Multiplayer.Rooms;
using Basil.Application.Runtime.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Runtime.Multiplayer.Events;

/// <summary>Something happened during a room round.</summary>
public abstract record RoomRoundEvent(Room Room, Round Round) : RoomEvent(Room);

/// <summary>What a player's departure did to the round in progress.</summary>
/// <param name="Round">The round in progress.</param>
/// <param name="AllLoaded">Whether every player still playing had now loaded; reported once per round.</param>
/// <param name="AllSkipped">Whether every player still playing had now skipped the intro; reported once per round.</param>
/// <param name="Completed">Whether the round ended because nobody plays any more.</param>
public sealed record RoomRoundProgress(Round Round, bool AllLoaded, bool AllSkipped, bool Completed);

/// <summary>A round started.</summary>
/// <remarks>When nobody plays, the round has already ended (<c>Round.EndedAt</c> is set).</remarks>
/// <param name="Room">The room.</param>
/// <param name="Round">The round.</param>
/// <param name="Players">The players taking part.</param>
public sealed record RoomRoundStarted(
	Room Room,
	Round Round,
	IReadOnlyList<BanchoConnection> Players)
	: RoomRoundEvent(Room, Round);

/// <summary>A round was aborted.</summary>
public sealed record RoomRoundAborted(Room Room, Round Round) : RoomRoundEvent(Room, Round);

/// <summary>A round ended because every player completed it.</summary>
/// <param name="Room">The room.</param>
/// <param name="Round">The round.</param>
/// <param name="Slot">The number of the slot whose action completed it.</param>
public sealed record RoomRoundCompleted(Room Room, Round Round, int Slot) : RoomRoundEvent(Room, Round);

/// <summary>A player finished loading the beatmap.</summary>
/// <param name="Room">The room.</param>
/// <param name="Round">The round.</param>
/// <param name="Slot">The slot number, from 1 to 16.</param>
public sealed record RoomRoundPlayerLoaded(Room Room, Round Round, int Slot) : RoomRoundEvent(Room, Round);

/// <summary>The last player finished loading, so every player has loaded.</summary>
/// <param name="Room">The room.</param>
/// <param name="Round">The round.</param>
/// <param name="Slot">The number of the slot whose action completed it.</param>
public sealed record RoomRoundAllLoaded(Room Room, Round Round, int Slot) : RoomRoundEvent(Room, Round);

/// <summary>A player asked to skip the intro.</summary>
/// <param name="Room">The room.</param>
/// <param name="Round">The round.</param>
/// <param name="Slot">The slot number, from 1 to 16.</param>
public sealed record RoomRoundPlayerSkipped(Room Room, Round Round, int Slot) : RoomRoundEvent(Room, Round);

/// <summary>The last player asked to skip the intro, so the intro is skipped.</summary>
/// <param name="Room">The room.</param>
/// <param name="Round">The round.</param>
/// <param name="Slot">The number of the slot whose action completed it.</param>
public sealed record RoomRoundAllSkipped(Room Room, Round Round, int Slot) : RoomRoundEvent(Room, Round);

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
/// <param name="Score">The relation between the stored score and the round.</param>
public sealed record RoomRoundScoreSubmitted(Room Room, Round Round, UserRecord Player, RoundScore Score)
	: RoomRoundEvent(Room, Round);

/// <summary>A countdown started in a room.</summary>
/// <param name="Room">The room.</param>
/// <param name="Countdown">The countdown that started.</param>
public sealed record RoomCountdownStarted(Room Room, RoomCountdown Countdown) : RoomEvent(Room);

/// <summary>A countdown reached one of its announced marks.</summary>
/// <param name="Room">The room.</param>
/// <param name="Countdown">The countdown that reached the mark.</param>
/// <param name="Remaining">The time remaining at the mark.</param>
public sealed record RoomCountdownTicked(Room Room, RoomCountdown Countdown, TimeSpan Remaining) : RoomEvent(Room);

/// <summary>A countdown elapsed.</summary>
/// <param name="Room">The room.</param>
/// <param name="Countdown">The countdown that elapsed.</param>
/// <param name="StartResult">The result of starting a round, or <see cref="RoomOutcome.Ok" /> when no round was requested.</param>
public sealed record RoomCountdownElapsed(Room Room, RoomCountdown Countdown, RoomOutcome StartResult) : RoomEvent(Room);

/// <summary>A countdown was cancelled.</summary>
/// <param name="Room">The room.</param>
/// <param name="Countdown">The countdown that was cancelled.</param>
public sealed record RoomCountdownCancelled(Room Room, RoomCountdown Countdown) : RoomEvent(Room);