namespace Basil.Domain.Game;

/// <summary>How the players of a match are grouped; the values carry the game numbering.</summary>
public enum GameTeamType : byte
{
	/// <summary>Every player plays for themselves.</summary>
	HeadToHead = 0,

	/// <summary>Players take turns on the same play, sharing one result.</summary>
	TagCoop = 1,

	/// <summary>Players are split into a blue and a red team.</summary>
	TeamVs = 2,

	/// <summary>Two teams, where the players of each team take turns on the same play.</summary>
	TagTeamVs = 3
}

/// <summary>Reads the properties of a <see cref="GameTeamType"/>.</summary>
public static class GameTeamTypeExtensions
{
	extension(GameTeamType type)
	{
		/// <summary>Tells whether players are split into blue and red teams.</summary>
		public bool IsTeamMode()
		{
			return type is GameTeamType.TeamVs or GameTeamType.TagTeamVs;
		}

		/// <summary>Tells whether players take turns on the same play.</summary>
		public bool IsTagMode()
		{
			return type is GameTeamType.TagCoop or GameTeamType.TagTeamVs;
		}
	}
}