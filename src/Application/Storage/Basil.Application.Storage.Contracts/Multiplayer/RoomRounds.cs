using Basil.Domain.Multiplayer;

namespace Basil.Application.Storage.Contracts.Multiplayer;

/// <summary>The rounds of a room: the last one played and the countdown to the next.</summary>
public sealed class RoomRounds
{
	/// <summary>Gets the most recently started round, or <see langword="null" /> before the first round.</summary>
	/// <remarks>Stays set after the round ends, until the next round starts.</remarks>
	public Round? LastRound { get; internal set; }

	/// <summary>Gets the round currently being played, or <see langword="null" /> when none is in progress.</summary>
	public Round? CurrentRound => LastRound is { EndedAt: null } round ? round : null;

	/// <summary>Gets a value that indicates whether a round is currently in progress.</summary>
	public bool InProgress => CurrentRound is not null;

	/// <summary>Gets when the running countdown ends, or <see langword="null" /> when none is running.</summary>
	public DateTimeOffset? CountdownEndsAt { get; internal set; }

	/// <summary>The running countdown, to stop when it is cancelled or replaced.</summary>
	internal IDisposable? CountdownTimer { get; set; }

	/// <summary>Whether the running countdown starts the round.</summary>
	internal bool CountdownStartsRound { get; set; }

	/// <summary>Whether every playing player of the current round has already been announced as loaded.</summary>
	internal bool AllLoadedAnnounced { get; set; }

	/// <summary>Whether every playing player of the current round has already been announced as having skipped the intro.</summary>
	internal bool AllSkippedAnnounced { get; set; }
}
