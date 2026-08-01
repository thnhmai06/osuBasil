namespace Basil.Domain.Multiplayer;

/// <summary>
///     Represents the team a player is assigned to in a team-based match.
/// </summary>
public enum MatchTeam : byte
{
	/// <summary>The player is not on a team.</summary>
	Neutral = 0, // no team
	/// <summary>The player is on the blue team.</summary>
	Blue = 1,
	/// <summary>The player is on the red team.</summary>
	Red = 2
}

/// <summary>
///     Specifies how the winner of a multiplayer match is decided.
/// </summary>
public enum MatchWinCondition : byte
{
	/// <summary>The match is decided by the total score of each team.</summary>
	Score = 0,
	/// <summary>The match is decided by the accuracy of each team.</summary>
	Accuracy = 1,
	/// <summary>The match is decided by the combo of each team.</summary>
	Combo = 2,
	/// <summary>The match is decided by the ScoreV2 scoring rules.</summary>
	ScoreV2 = 3
}

/// <summary>
///     Specifies how players are grouped into teams for a multiplayer match.
/// </summary>
public enum MatchTeamType : byte
{
	/// <summary>Each player competes individually against the others.</summary>
	HeadToHead = 0,
	/// <summary>All players share a single score as a tag team.</summary>
	TagCoop = 1,
	/// <summary>Players are split into a blue and a red team.</summary>
	TeamVs = 2,
	/// <summary>Players are split into teams that share scores as tag teams.</summary>
	TagTeamVs = 3
}
