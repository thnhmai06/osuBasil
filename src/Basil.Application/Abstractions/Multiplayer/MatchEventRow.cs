namespace Basil.Application.Abstractions.Multiplayer;

public enum MatchEventType
{
    Created = 0,
    RefAdded = 1,
    RefRemoved = 2,
    HostGranted = 3,
    PlayerJoined = 4,
    PlayerLeft = 5,
    Kicked = 6,
    Closed = 7,
}

public sealed record MatchEventRow(
    int MatchId,
    int EventType,
    int? ActorUserId,
    string? ActorUserName,
    int? TargetUserId,
    string? TargetUserName,
    DateTime Timestamp,
    string? Detail);
