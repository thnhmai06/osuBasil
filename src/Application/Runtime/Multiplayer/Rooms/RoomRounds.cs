using Basil.Domain.Multiplayer;

namespace Basil.Application.Runtime.Multiplayer.Rooms;

/// <summary>The rounds and countdown of a room.</summary>
public sealed class RoomRounds
{
	/// <summary>How long after a round ended the room still waits for the scores of the players who played it.</summary>
	public static readonly TimeSpan ScoreWait = TimeSpan.FromSeconds(10);

	/// <summary>Gets the most recently started round, or <see langword="null" /> before the first round.</summary>
	/// <remarks>Stays set after the round ends, until the next round starts.</remarks>
	public Round? LastRound { get; internal set; }

	/// <summary>Gets the round currently being played, or <see langword="null" /> when none is in progress.</summary>
	public Round? CurrentRound => LastRound is { EndedAt: null } round ? round : null;

	/// <summary>Gets a value that indicates whether a round is currently in progress.</summary>
	public bool InProgress => CurrentRound is not null;

	/// <summary>Whether every playing player of the current round has already been announced as loaded.</summary>
	internal bool AllLoadedAnnounced { get; set; }

	/// <summary>Whether every playing player of the current round has already been announced as having skipped the intro.</summary>
	internal bool AllSkippedAnnounced { get; set; }

	/// <summary>What the latest round still waits for, or <see langword="null" /> before the first round.</summary>
	internal RoundSettlement? Settlement { get; set; }

	/// <summary>Gets the countdown running for the room, or <see langword="null" /> when none is running.</summary>
	public RoomCountdown? Countdown { get; internal set; }

	/// <summary>The timers that announce the countdown marks and its end.</summary>
	internal ITimer[] CountdownTimers { get; set; } = [];
}