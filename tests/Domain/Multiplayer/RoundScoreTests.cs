using Basil.Domain.Common;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Domain.Tests.Multiplayer;

/// <summary>Verifies that a score's team agrees with the kind of round it was played in, however it is built.</summary>
public class RoundScoreTests
{
	private static Round MakeRound(GameTeamType teamType)
	{
		return new Round
		{
			Index = 1,
			BeatmapHash = new Md5("0123456789abcdef0123456789abcdef"),
			Settings = new MatchSettings { TeamType = teamType },
			StartedAt = DateTimeOffset.UnixEpoch,
			EndedAt = null,
			Match = new MatchRecord
			{
				Id = 1,
				Value = new MatchData
				{
					Name = "match",
					Creator = new UserRecord { Id = 1, Value = new UserData { Name = "creator" } },
					StartedAt = DateTimeOffset.UnixEpoch,
					EndedAt = null
				}
			}
		};
	}

	private static ScoreRecord MakeScore()
	{
		return new ScoreRecord
		{
			Id = 1,
			Value = new ScoreData(null, null, GameMode.Standard, GameMods.NoMod, new HitCounts(1, 0, 0, 0, 0, 0), 1, 1,
				1, Grade.A, true, false, DateTimeOffset.UnixEpoch)
		};
	}

	[Fact]
	public void Constructor_TeamInTeamVersus_IsAccepted()
	{
		var score = new RoundScore(MakeScore(), MakeRound(GameTeamType.TeamVs), GameTeam.Red);

		Assert.Equal(GameTeam.Red, score.Team);
	}

	[Fact]
	public void Constructor_NoTeamInHeadToHead_IsAccepted()
	{
		var score = new RoundScore(MakeScore(), MakeRound(GameTeamType.HeadToHead), null);

		Assert.Null(score.Team);
	}

	[Theory]
	[InlineData(GameTeamType.TeamVs, null)]
	[InlineData(GameTeamType.TagTeamVs, null)]
	[InlineData(GameTeamType.HeadToHead, GameTeam.Blue)]
	public void Constructor_TeamNotMatchingTheRound_ThrowsArgumentException(GameTeamType type, GameTeam? team)
	{
		Assert.Throws<ArgumentException>(() => new RoundScore(MakeScore(), MakeRound(type), team));
	}
}
