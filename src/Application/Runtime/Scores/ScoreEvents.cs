using Basil.Application.Runtime.Events;
using Basil.Application.Runtime.Multiplayer.Rooms;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Application.Runtime.Scores;

/// <summary>Something happened to scores.</summary>
public abstract record ScoreEvent : Event;

/// <summary>A score was stored.</summary>
/// <param name="Player">The user who set the score.</param>
/// <param name="Score">The stored score.</param>
/// <param name="Stats">The player's statistics in the score's mode after the score.</param>
/// <param name="RoundScore">
///     The relation between the score and the round it was played in, or <see langword="null" /> when the
///     score belongs to no round.
/// </param>
/// <param name="Room">
///     The room whose latest round the score was played in, or <see langword="null" /> when the score
///     belongs to no round.
/// </param>
public sealed record ScoreSubmitted(
	UserRecord Player,
	ScoreRecord Score,
	UserStats Stats,
	RoundScore? RoundScore,
	Room? Room)
	: ScoreEvent;