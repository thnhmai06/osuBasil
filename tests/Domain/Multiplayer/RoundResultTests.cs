using Basil.Domain.Common;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Domain.Tests.Multiplayer;

/// <summary>Verifies how a round's scores decide a winner, a draw or no result.</summary>
public class RoundResultTests
{
	private static Round MakeRound(GameTeamType teamType, GameWinCondition condition = GameWinCondition.Score)
	{
		return new Round
		{
			Index = 1,
			BeatmapHash = new Md5("0123456789abcdef0123456789abcdef"),
			Settings = new MatchSettings { TeamType = teamType, WinCondition = condition },
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

	private static RoundScore Play(Round round, int id, int userId, int totalScore, bool passed = true,
		GameTeam? team = null)
	{
		var score = new ScoreRecord
		{
			Id = id,
			Value = new ScoreData(userId, null, GameMode.Standard, GameMods.NoMod, new HitCounts(1, 0, 0, 0, 0, 0),
				totalScore, 1, 1, Grade.A, passed, false, DateTimeOffset.UnixEpoch)
		};
		return new RoundScore(score, round, team);
	}

	[Fact]
	public void Decide_NoScores_ReturnsNull()
	{
		Assert.Null(RoundResult.Decide(MakeRound(GameTeamType.HeadToHead), []));
	}

	[Fact]
	public void Decide_TeamModeRound_ReturnsTeamResult()
	{
		var round = MakeRound(GameTeamType.TeamVs);

		Assert.IsType<TeamVersusResult>(RoundResult.Decide(round, [Play(round, 1, 1, 100, team: GameTeam.Red)]));
	}

	[Fact]
	public void HeadToHead_HigherScoreWins_NotDrawAndMarginIsDifference()
	{
		var round = MakeRound(GameTeamType.HeadToHead);

		var result = new HeadToHeadResult(round, [Play(round, 1, 10, 100), Play(round, 2, 20, 300)]);

		Assert.Equal(20, result.WinnerId);
		Assert.False(result.IsDraw);
		Assert.Equal(200, result.Margin);
	}

	[Fact]
	public void HeadToHead_EqualScores_IsDrawWithoutWinner()
	{
		var round = MakeRound(GameTeamType.HeadToHead);

		var result = new HeadToHeadResult(round, [Play(round, 1, 10, 100), Play(round, 2, 20, 100)]);

		Assert.True(result.IsDraw);
		Assert.Null(result.WinnerId);
		Assert.Equal(0, result.Margin);
	}

	[Fact]
	public void HeadToHead_SoleScoreOfZero_WinsAndIsNotDraw()
	{
		var round = MakeRound(GameTeamType.HeadToHead);

		var result = new HeadToHeadResult(round, [Play(round, 1, 10, 0)]);

		Assert.Equal(10, result.WinnerId);
		Assert.False(result.IsDraw);
	}

	[Fact]
	public void HeadToHead_PassedZeroAgainstFailed_PassedPlayerWins()
	{
		var round = MakeRound(GameTeamType.HeadToHead);

		var result = new HeadToHeadResult(round, [Play(round, 1, 10, 0), Play(round, 2, 20, 900, passed: false)]);

		Assert.Equal(10, result.WinnerId);
		Assert.False(result.IsDraw);
	}

	[Fact]
	public void HeadToHead_EveryoneFailed_HigherFailedScoreWins()
	{
		var round = MakeRound(GameTeamType.HeadToHead);

		var result = new HeadToHeadResult(round,
			[Play(round, 1, 10, 100, passed: false), Play(round, 2, 20, 500, passed: false)]);

		Assert.Equal(20, result.WinnerId);
	}

	[Fact]
	public void HeadToHead_TeamModeRound_Throws()
	{
		var round = MakeRound(GameTeamType.TeamVs);

		Assert.Throws<ArgumentException>(() => new HeadToHeadResult(round, []));
	}

	[Fact]
	public void TeamVersus_HigherTeamTotalWins()
	{
		var round = MakeRound(GameTeamType.TeamVs);

		var result = new TeamVersusResult(round,
		[
			Play(round, 1, 10, 100, team: GameTeam.Red), Play(round, 2, 11, 150, team: GameTeam.Red),
			Play(round, 3, 20, 200, team: GameTeam.Blue)
		]);

		Assert.Equal(GameTeam.Red, result.Winner);
		Assert.False(result.IsDraw);
		Assert.Equal(50, result.Margin);
	}

	[Fact]
	public void TeamVersus_EqualTotals_IsDrawWithoutWinner()
	{
		var round = MakeRound(GameTeamType.TeamVs);

		var result = new TeamVersusResult(round,
			[Play(round, 1, 10, 100, team: GameTeam.Red), Play(round, 2, 20, 100, team: GameTeam.Blue)]);

		Assert.True(result.IsDraw);
		Assert.Null(result.Winner);
	}

	[Fact]
	public void TeamVersus_SoleTeamWithZeroTotal_WinsAndIsNotDraw()
	{
		var round = MakeRound(GameTeamType.TeamVs);

		var result = new TeamVersusResult(round, [Play(round, 1, 10, 0, team: GameTeam.Blue)]);

		Assert.Equal(GameTeam.Blue, result.Winner);
		Assert.False(result.IsDraw);
	}

	[Fact]
	public void TeamVersus_TeamWithoutScores_IsPresentWithZero()
	{
		var round = MakeRound(GameTeamType.TeamVs);

		var result = new TeamVersusResult(round, [Play(round, 1, 10, 100, team: GameTeam.Blue)]);

		Assert.Equal(0, result.TeamScore[GameTeam.Red]);
		Assert.Empty(result.TeamScores[GameTeam.Red]);
	}

	[Fact]
	public void TeamVersus_TeamOfOnlyFailedPlayers_OpposingTeamWins()
	{
		var round = MakeRound(GameTeamType.TeamVs);

		var result = new TeamVersusResult(round,
			[Play(round, 1, 10, 900, passed: false, team: GameTeam.Red), Play(round, 2, 20, 10, team: GameTeam.Blue)]);

		Assert.Equal(GameTeam.Blue, result.Winner);
	}

	[Fact]
	public void TeamVersus_HeadToHeadRound_Throws()
	{
		var round = MakeRound(GameTeamType.HeadToHead);

		Assert.Throws<ArgumentException>(() => new TeamVersusResult(round, []));
	}
}
