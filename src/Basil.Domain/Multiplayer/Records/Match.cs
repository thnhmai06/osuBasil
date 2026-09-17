using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;

namespace Basil.Domain.Multiplayer.Records;

/// <summary>
///     A match record as read back for report and management purposes.
/// </summary>
public sealed class Match : IEquatable<Match>
{
	public required int Id { get; init; }
	public required string Name { get; set; }
	public required DateTimeOffset CreatedAt { get; init; }
	public required DateTimeOffset? EndedAt { get; set; }

	public bool Equals(Match? other)
	{
		if (other is null) return false;
		return Id == other.Id;
	}

	public override bool Equals(object? obj)
	{
		return obj is Match other && Equals(other);
	}

	public override int GetHashCode()
	{
		return Id;
	}
}

public sealed class MatchSettings
{
	/// <summary>Gets or sets the currently selected beatmap, or <see langword="null" /> when none is chosen.</summary>
	public Beatmap? Beatmap { get; set; }

	/// <summary>Gets or sets the mods applied to the whole room.</summary>
	public Mods Mods { get; set; } = Mods.NoMod;

	/// <summary>Gets or sets the game mode the room plays in.</summary>
	public GameMode Mode { get; set; } = GameMode.Standard;

	/// <summary>Gets or sets a value that indicates whether freemod mode is enabled.</summary>
	public bool Freemods { get; set; }

	/// <summary>Gets or sets the team arrangement used for the room.</summary>
	public MatchTeamType TeamType { get; set; } = MatchTeamType.HeadToHead;

	/// <summary>Gets or sets the condition that decides the winner of a round.</summary>
	public MatchWinCondition WinCondition { get; set; } = MatchWinCondition.Score;

	/// <summary>Gets the room's random seed, broadcast to clients as part of the match's data.</summary>
	public int Seed { get; init; }
}

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