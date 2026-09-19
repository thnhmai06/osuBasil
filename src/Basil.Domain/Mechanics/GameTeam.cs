namespace Basil.Domain.Mechanics;

/// <summary>
///     Represents the team a player is assigned to in a team-based match.
/// </summary>
public enum GameTeam : byte
{
	/// <summary>The player is not on a team.</summary>
	Neutral = 0, // no team

	/// <summary>The player is on the blue team.</summary>
	Blue = 1,

	/// <summary>The player is on the red team.</summary>
	Red = 2
}