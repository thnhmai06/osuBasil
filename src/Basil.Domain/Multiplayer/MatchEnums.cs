namespace Basil.Domain.Multiplayer;

/// <summary>Ported from app/objects/match.py's MatchTeam (IntEnum).</summary>
public enum MatchTeam : byte
{
    Neutral = 0, // no team
    Blue = 1,
    Red = 2
}

/// <summary>Ported from app/objects/match.py's MatchWinCondition (IntEnum).</summary>
public enum MatchWinCondition : byte
{
    Score = 0,
    Accuracy = 1,
    Combo = 2,
    ScoreV2 = 3
}

/// <summary>Ported from app/objects/match.py's MatchTeamType (IntEnum).</summary>
public enum MatchTeamType : byte
{
    HeadToHead = 0,
    TagCoop = 1,
    TeamVs = 2,
    TagTeamVs = 3
}