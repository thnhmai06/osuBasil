namespace Bancho.Domain.Multiplayer;

/// <summary>Ported from app/objects/match.py's MatchTeams (IntEnum).</summary>
public enum MatchTeams
{
    Neutral = 0,
    Blue = 1,
    Red = 2
}

/// <summary>Ported from app/objects/match.py's MatchWinConditions (IntEnum).</summary>
public enum MatchWinConditions
{
    Score = 0,
    Accuracy = 1,
    Combo = 2,
    ScoreV2 = 3
}

/// <summary>Ported from app/objects/match.py's MatchTeamTypes (IntEnum).</summary>
public enum MatchTeamTypes
{
    HeadToHead = 0,
    TagCoop = 1,
    TeamVs = 2,
    TagTeamVs = 3
}