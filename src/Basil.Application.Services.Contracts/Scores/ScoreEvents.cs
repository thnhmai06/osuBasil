using Basil.Application.Services.Contracts.Events;
using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Application.Services.Contracts.Scores;

/// <summary>Something happened to scores.</summary>
public abstract record ScoreEvent : Event;

/// <summary>A score was stored.</summary>
/// <param name="Player">The user who set the score.</param>
/// <param name="Score">The stored score.</param>
/// <param name="Stats">The player's statistics in the score's mode after the score.</param>
/// <param name="Room">
///     The room whose latest round the score was played in, or <see langword="null" /> when the score
///     belongs to no round.
/// </param>
public sealed record ScoreSubmitted(User Player, Score Score, UserStats Stats, Room? Room) : ScoreEvent;