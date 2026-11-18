using Basil.Application.Shared.Events;
using Basil.Domain.Game.Scores;
using Basil.Domain.Users;

namespace Basil.Application.Scores.Objects.Events;

public abstract record ScoreEvent : Event;

public sealed record ScoreSubmitted(UserRecord Player, ScoreRecord Score, UserStats Stats) : ScoreEvent;