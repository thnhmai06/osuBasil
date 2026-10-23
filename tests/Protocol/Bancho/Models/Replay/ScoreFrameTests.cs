using Basil.Protocol.Bancho.Models.Replay;

namespace Basil.Protocol.Bancho.Tests.Models.Replay;

/// <summary>
///     Verifies the value semantics of the <see cref="ScoreFrame" /> wire record: two frames with the same
///     property values are equal, a differing property makes them unequal, and the <c>with</c> expression
///     produces a modified copy without touching the original.
/// </summary>
public class ScoreFrameTests
{
	private static ScoreFrame Frame => new()
	{
		Time = 1000,
		Id = 5,
		Num300 = 2,
		Num100 = 3,
		Num50 = 4,
		NumGeki = 5,
		NumKatu = 6,
		NumMiss = 7,
		TotalScore = 287_454_020,
		MaxCombo = 300,
		CurrentCombo = 11,
		Perfect = false,
		CurrentHp = 142,
		TagByte = 9,
		ScoreV2 = true,
		ComboPortion = 0.5,
		BonusPortion = null
	};

	[Fact]
	public void ScoreFrame_TwoInstancesWithEqualPropertyValues_AreEqual()
	{
		Assert.Equal(Frame, Frame);
	}

	[Fact]
	public void ScoreFrame_TwoInstancesWithADifferingPropertyValue_AreNotEqual()
	{
		var changed = Frame with { TotalScore = 287_454_020 + 1 };

		Assert.NotEqual(Frame, changed);
	}

	[Fact]
	public void WithExpression_ChangesOnlyTheTargetedProperty()
	{
		var changed = Frame with { Time = 2_500 };

		Assert.Equal(2_500, changed.Time);
		Assert.Equal(1_000, Frame.Time);
		Assert.Equal(2, changed.Num300);
		Assert.Equal(3, changed.Num100);
		Assert.Equal(4, changed.Num50);
		Assert.Equal(5, changed.NumGeki);
		Assert.Equal(6, changed.NumKatu);
		Assert.Equal(7, changed.NumMiss);
		Assert.Equal(287_454_020, changed.TotalScore);
		Assert.Equal(300, changed.MaxCombo);
		Assert.Equal(11, changed.CurrentCombo);
		Assert.False(changed.Perfect);
		Assert.Equal(142, changed.CurrentHp);
		Assert.Equal(9, changed.TagByte);
		Assert.True(changed.ScoreV2);
		Assert.Equal(0.5, changed.ComboPortion);
		Assert.Null(changed.BonusPortion);
	}
}