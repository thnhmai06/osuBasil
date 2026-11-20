namespace Basil.Domain.Game;

public enum GameTeamType : byte
{
	HeadToHead = 0,

	TagCoop = 1,

	TeamVs = 2,

	TagTeamVs = 3
}

public static class GameTeamTypeExtensions
{
	extension(GameTeamType type)
	{
		public bool IsTeamMode()
		{
			return type is GameTeamType.TeamVs or GameTeamType.TagTeamVs;
		}

		public bool IsTagMode()
		{
			return type is GameTeamType.TagCoop or GameTeamType.TagTeamVs;
		}
	}
}