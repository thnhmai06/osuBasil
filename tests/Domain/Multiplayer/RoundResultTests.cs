using Basil.Domain.Common;
using Basil.Domain.Cryptography;
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
			Value = new ScoreData(userId, null, GameMode.Standard, GameMods.NoMod, new ScoreState(
				new HitCounts(1, 0, 0, 0, 0, 0),
				totalScore, 1, 1, false), Grade.A, passed, DateTimeOffset.UnixEpoch)
		};
		return new RoundScore(score, round, team);
	}

	[Fact]
	public void NoScores_ReturnsNull()
	{
		Assert.Null(RoundResult.Decide(MakeRound(GameTeamType.HeadToHead), []));
	}

	[Fact]
	public void HeadToHead_RestoreGivesBack()
	{
		var round = MakeRound(GameTeamType.HeadToHead);
		var low = Play(round, 1, 1, 100);
		var high = Play(round, 2, 2, 900);

		var restored = Assert.IsType<HeadToHeadResult>(RoundResult.Restore(round, [low, high], [2, 1], 800, false, 2, null, null, null));

		Assert.Equal(2, restored.WinnerId);
		Assert.Equal(800, restored.Margin);
		Assert.False(restored.IsDraw);
		Assert.Equal([2, 1], restored.Scores.Select(s => s.Score.Id));
	}

	[Fact]
	public void TeamRestore_GivesBackDecision()
	{
		var round = MakeRound(GameTeamType.TeamVs);
		var red = Play(round, 1, 1, 100, team: GameTeam.Red);
		var blue = Play(round, 2, 2, 900, team: GameTeam.Blue);

		var restored = Assert.IsType<TeamVersusResult>(RoundResult.Restore(round, [red, blue], [2, 1], 800, false, null, GameTeam.Blue, 100, 900));

		Assert.Equal(GameTeam.Blue, restored.Winner);
		Assert.Equal(800, restored.Margin);
		Assert.Equal(100, restored.TeamScore[GameTeam.Red]);
		Assert.Equal(900, restored.TeamScore[GameTeam.Blue]);
		Assert.Equal([2, 1], restored.Scores.Select(s => s.Score.Id));
	}

	[Fact]
	public void Restore_UsesStoredWinner()
	{
		var round = MakeRound(GameTeamType.HeadToHead);
		var low = Play(round, 1, 1, 100);
		var high = Play(round, 2, 2, 900);

		var restored = Assert.IsType<HeadToHeadResult>(RoundResult.Restore(round, [low, high],
			[1, 2], 800, false, 1, null, null, null));

		Assert.Equal(1, restored.WinnerId);
		Assert.Equal([1, 2], restored.Scores.Select(s => s.Score.Id));
	}

	[Fact]
	public void Restore_StoredDrawHasNoWinner()
	{
		var round = MakeRound(GameTeamType.HeadToHead);
		var low = Play(round, 1, 1, 100);
		var high = Play(round, 2, 2, 900);

		var restored = Assert.IsType<HeadToHeadResult>(RoundResult.Restore(round, [low, high], [2, 1], 0, true, null, null, null, null));

		Assert.Null(restored.WinnerId);
		Assert.True(restored.IsDraw);
		Assert.Equal(0, restored.Margin);
	}

	[Fact]
	public void Restore_StoredWinnerBeatsRankingByOrder()
	{
		var round = MakeRound(GameTeamType.HeadToHead);
		var low = Play(round, 1, 1, 100);
		var high = Play(round, 2, 2, 900);

		var restored = Assert.IsType<HeadToHeadResult>(RoundResult.Restore(round, [low, high], [2, 1], 800, false,
			1, null, null, null));

		Assert.Equal(1, restored.WinnerId);
	}

	[Fact]
	public void TeamModeRound_ReturnsTeamResult()
	{
		var round = MakeRound(GameTeamType.TeamVs);

		Assert.IsType<TeamVersusResult>(RoundResult.Decide(round, [Play(round, 1, 1, 100, team: GameTeam.Red)]));
	}

	[Fact]
	public void HigherScore_WinsByMargin()
	{
		var round = MakeRound(GameTeamType.HeadToHead);

		var result = new HeadToHeadResult(round, [Play(round, 1, 10, 100), Play(round, 2, 20, 300)]);

		Assert.Equal(20, result.WinnerId);
		Assert.False(result.IsDraw);
		Assert.Equal(200, result.Margin);
	}

	[Fact]
	public void EqualScores_DrawWithoutWinner()
	{
		var round = MakeRound(GameTeamType.HeadToHead);

		var result = new HeadToHeadResult(round, [Play(round, 1, 10, 100), Play(round, 2, 20, 100)]);

		Assert.True(result.IsDraw);
		Assert.Null(result.WinnerId);
		Assert.Equal(0, result.Margin);
	}

	[Fact]
	public void SoleZeroScore_WinsNotDraw()
	{
		var round = MakeRound(GameTeamType.HeadToHead);

		var result = new HeadToHeadResult(round, [Play(round, 1, 10, 0)]);

		Assert.Equal(10, result.WinnerId);
		Assert.False(result.IsDraw);
	}

	[Fact]
	public void PassedZeroOnly_PasserWins()
	{
		var round = MakeRound(GameTeamType.HeadToHead);

		var result = new HeadToHeadResult(round, [Play(round, 1, 10, 0), Play(round, 2, 20, 900, false)]);

		Assert.Equal(10, result.WinnerId);
		Assert.False(result.IsDraw);
	}

	[Fact]
	public void AllFailed_HigherScoreWins()
	{
		var round = MakeRound(GameTeamType.HeadToHead);

		var result = new HeadToHeadResult(round,
			[Play(round, 1, 10, 100, false), Play(round, 2, 20, 500, false)]);

		Assert.Equal(20, result.WinnerId);
	}

	[Fact]
	public void TeamModeRound_Throws()
	{
		var round = MakeRound(GameTeamType.TeamVs);

		Assert.Throws<ArgumentException>(() => new HeadToHeadResult(round, []));
	}

	[Fact]
	public void HigherTeamTotal_Wins()
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
	public void EqualTotals_DrawWithoutWinner()
	{
		var round = MakeRound(GameTeamType.TeamVs);

		var result = new TeamVersusResult(round,
			[Play(round, 1, 10, 100, team: GameTeam.Red), Play(round, 2, 20, 100, team: GameTeam.Blue)]);

		Assert.True(result.IsDraw);
		Assert.Null(result.Winner);
	}

	[Fact]
	public void SoleZeroTotal_WinsNotDraw()
	{
		var round = MakeRound(GameTeamType.TeamVs);

		var result = new TeamVersusResult(round, [Play(round, 1, 10, 0, team: GameTeam.Blue)]);

		Assert.Equal(GameTeam.Blue, result.Winner);
		Assert.False(result.IsDraw);
	}

	[Fact]
	public void ScorelessTeam_PresentWithZero()
	{
		var round = MakeRound(GameTeamType.TeamVs);

		var result = new TeamVersusResult(round, [Play(round, 1, 10, 100, team: GameTeam.Blue)]);

		Assert.Equal(0, result.TeamScore[GameTeam.Red]);
		Assert.Empty(result.TeamScores[GameTeam.Red]);
	}

	[Fact]
	public void AllFailedTeam_OpponentWins()
	{
		var round = MakeRound(GameTeamType.TeamVs);

		var result = new TeamVersusResult(round,
			[Play(round, 1, 10, 900, false, GameTeam.Red), Play(round, 2, 20, 10, team: GameTeam.Blue)]);

		Assert.Equal(GameTeam.Blue, result.Winner);
	}

	[Fact]
	public void HeadToHead_Throws()
	{
		var round = MakeRound(GameTeamType.HeadToHead);

		Assert.Throws<ArgumentException>(() => new TeamVersusResult(round, []));
	}
}