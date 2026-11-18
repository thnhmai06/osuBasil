
using Basil.Domain.Game.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Game.Scores;
using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Domain.Tests.Multiplayer;

public class RoundScoreTests
{
	private static Round MakeRound(GameTeamType teamType)
	{
		return new Round
		{
			Index = 1,
			BeatmapHash = new Md5Hash("0123456789abcdef0123456789abcdef"),
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
			Value = new ScoreData(null, null, GameMode.Standard, GameMods.NoMod, new ScoreState(
				new HitCounts(1, 0, 0, 0, 0, 0), 1, 1,
				1, false), Grade.A, true, DateTimeOffset.UnixEpoch)
		};
	}

	[Fact]
	public void TeamVersusTeam_Accepted()
	{
		var score = new RoundScore(MakeScore(), MakeRound(GameTeamType.TeamVs), GameTeam.Red);

		Assert.Equal(GameTeam.Red, score.Team);
	}

	[Fact]
	public void HeadToHead_NoTeamAccepted()
	{
		var score = new RoundScore(MakeScore(), MakeRound(GameTeamType.HeadToHead), null);

		Assert.Null(score.Team);
	}

	[Theory]
	[InlineData(GameTeamType.TeamVs, null)]
	[InlineData(GameTeamType.TagTeamVs, null)]
	[InlineData(GameTeamType.HeadToHead, GameTeam.Blue)]
	public void MismatchedTeam_Throws(GameTeamType type, GameTeam? team)
	{
		Assert.Throws<ArgumentException>(() => new RoundScore(MakeScore(), MakeRound(type), team));
	}
}