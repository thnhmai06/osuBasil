namespace Basil.Domain.Mechanics;

/// <summary>
///     Specifies how players are grouped into teams for a multiplayer match.
/// </summary>
public enum GameTeamType : byte
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

/// <summary>Reads the properties of a <see cref="GameTeamType" />.</summary>
public static class GameTeamTypeExtensions
{
	extension(GameTeamType type)
	{
		/// <summary>Tells whether players are split into opposing teams.</summary>
		public bool IsTeamMode()
		{
			return type is GameTeamType.TeamVs or GameTeamType.TagTeamVs;
		}

		/// <summary>Tells whether players take turns sharing a combo.</summary>
		public bool IsTagMode()
		{
			return type is GameTeamType.TagCoop or GameTeamType.TagTeamVs;
		}
	}
}
