using Basil.Application.Shared.Events;
using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Application.Scores.Objects.Events;

[EventLane(EventLane.Multiplayer)]
public abstract record ScoreEvent : Event;

public sealed record ScoreSubmitted(UserRecord Player, ScoreRecord Score, UserStats Stats) : ScoreEvent;