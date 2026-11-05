using Basil.Domain.Mechanics;
using Basil.Domain.Scores;

namespace Basil.Domain.Tests.Scores;

/// <summary>Verifies the validation of the scoring fields of a submitted play.</summary>
public class ScoreDataTests
{
	private static ScoreData Make(int? userId = null, GameMode mode = GameMode.Standard, Grade grade = Grade.S,
		int totalScore = 1000, ushort maxCombo = 10, ushort currentCombo = 10)
	{
		return new ScoreData(userId, null, mode, GameMods.NoMod, new ScoreState(new HitCounts(100, 0, 0, 0, 0, 0),
			totalScore,
			maxCombo, currentCombo, false), grade, true, DateTimeOffset.UnixEpoch);
	}

	[Fact]
	public void TotalScore_Negative_Constructor_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Make(totalScore: -1));
	}

	[Fact]
	public void TotalScore_Negative_With_Throws()
	{
		var score = Make();

		Assert.Throws<ArgumentOutOfRangeException>(() =>
			_ = score with { LastState = score.LastState with { TotalScore = -1 } });
	}

	[Fact]
	public void UserId_Zero_Constructor_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Make(0));
	}

	[Fact]
	public void UserId_Negative_Constructor_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Make(-1));
	}

	[Fact]
	public void UserId_Negative_With_Throws()
	{
		var score = Make(5);

		Assert.Throws<ArgumentOutOfRangeException>(() => _ = score with { UserId = -1 });
	}

	[Fact]
	public void UserId_Unknown_IsAccepted()
	{
		Assert.Null(Make().UserId);
	}

	[Fact]
	public void Mode_Undefined_Constructor_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Make(mode: (GameMode)200));
	}

	[Fact]
	public void Mode_Undefined_With_Throws()
	{
		var score = Make();

		Assert.Throws<ArgumentOutOfRangeException>(() => _ = score with { Mode = (GameMode)200 });
	}

	[Fact]
	public void Grade_Undefined_Constructor_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Make(grade: (Grade)200));
	}

	[Fact]
	public void Grade_Undefined_With_Throws()
	{
		var score = Make();

		Assert.Throws<ArgumentOutOfRangeException>(() => _ = score with { Grade = (Grade)200 });
	}
}