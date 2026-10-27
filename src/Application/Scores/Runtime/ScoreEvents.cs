using Basil.Application.Common.Events;
using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Application.Scores.Runtime;

/// <summary>Something happened to scores.</summary>
public abstract record ScoreEvent : Event;

/// <summary>A score was stored.</summary>
/// <param name="Player">The user who set the score.</param>
/// <param name="Score">The stored score.</param>
/// <param name="Stats">The player's statistics in the score's mode after the score.</param>
public sealed record ScoreSubmitted(UserRecord Player, ScoreRecord Score, UserStats Stats) : ScoreEvent;