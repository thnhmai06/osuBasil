namespace Basil.Domain.Game;

/// <summary>The side a player is on in a team match; the values carry the game numbering.</summary>
public enum GameTeam : byte
{
	/// <summary>The blue team.</summary>
	Blue = 1,

	/// <summary>The red team.</summary>
	Red = 2
}