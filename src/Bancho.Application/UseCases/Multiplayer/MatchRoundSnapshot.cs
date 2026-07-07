using Bancho.Domain;

namespace Bancho.Application.UseCases.Multiplayer;

/// <summary>
/// Everything MatchScoringService's lockless scoring poll needs, captured under MatchSession.Lock
/// at MATCH_COMPLETE time before the lock is released. Snapshotting avoids re-reading match state
/// that a concurrent handler could otherwise mutate during the up-to-10s poll (see MatchSession's
/// doc comment on scrim's lock/launch ordering).
/// </summary>
public sealed record MatchRoundSnapshot(
    IReadOnlyList<(int PlayerId, MatchTeams Team)> WasPlaying,
    string MapMd5,
    MatchTeamTypes TeamType,
    MatchWinConditions WinCondition,
    string MatchName);
