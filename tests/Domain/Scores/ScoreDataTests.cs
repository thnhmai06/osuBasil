using Basil.Domain.Mechanics;
using Basil.Domain.Scores;

namespace Basil.Domain.Tests.Scores;

/// <summary>Verifies the validation of the scoring fields of a submitted play.</summary>
public class ScoreDataTests
{
	private static ScoreData Make(int? userId = null, GameMode mode = GameMode.Standard, Grade grade = Grade.S,
		int totalScore = 1000, short maxCombo = 10, short currentCombo = 10)
	{
		return new ScoreData(userId, null, mode, GameMods.NoMod, new HitCounts(100, 0, 0, 0, 0, 0), totalScore,
			maxCombo, currentCombo, grade, true, false, DateTimeOffset.UnixEpoch);
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

		Assert.Throws<ArgumentOutOfRangeException>(() => _ = score with { TotalScore = -1 });
	}

	[Fact]
	public void MaxCombo_Negative_Constructor_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Make(maxCombo: -1));
	}

	[Fact]
	public void MaxCombo_Negative_With_Throws()
	{
		var score = Make();

		Assert.Throws<ArgumentOutOfRangeException>(() => _ = score with { MaxCombo = -1 });
	}

	[Fact]
	public void CurrentCombo_Negative_Constructor_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Make(currentCombo: -1));
	}

	[Fact]
	public void CurrentCombo_Negative_With_Throws()
	{
		var score = Make();

		Assert.Throws<ArgumentOutOfRangeException>(() => _ = score with { CurrentCombo = -1 });
	}

	[Fact]
	public void UserId_Zero_Constructor_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Make(userId: 0));
	}

	[Fact]
	public void UserId_Negative_Constructor_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Make(userId: -1));
	}

	[Fact]
	public void UserId_Negative_With_Throws()
	{
		var score = Make(userId: 5);

		Assert.Throws<ArgumentOutOfRangeException>(() => _ = score with { UserId = -1 });
	}

	[Fact]
	public void UserId_Unknown_IsAccepted()
	{
		Assert.Null(Make(userId: null).UserId);
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