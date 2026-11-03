using Basil.Domain.Scores;
using Basil.Domain.Client;

namespace Basil.Domain.Tests.Client;

/// <summary>
///     Verifies the value semantics of the <see cref="ScoreFrame" /> wire record: two frames with the same
///     property values are equal, a differing property makes them unequal, and the <c>with</c> expression
///     produces a modified copy without touching the original.
/// </summary>
public class ScoreFrameTests
{
	private static ScoreFrame Frame => new(Time: TimeSpan.FromMilliseconds(1000), Id: 5, State: new ScoreState(new HitCounts(Num300: 2, Num100: 3, Num50: 4, NumGeki: 5, NumKatu: 6, NumMiss: 7), 287_454_020, 300, 11, false), Health: 142, TagByte: 9, Sv2Portion: new ScoreV2Portion(0.5, 0.25));

	[Fact]
	public void ScoreFrame_TwoInstancesWithEqualPropertyValues_AreEqual()
	{
		Assert.Equal(Frame, Frame);
	}

	[Fact]
	public void ScoreFrame_TwoInstancesWithADifferingPropertyValue_AreNotEqual()
	{
		var changed = Frame with { State = Frame.State with { TotalScore = 287_454_020 + 1 } };

		Assert.NotEqual(Frame, changed);
	}

	[Fact]
	public void WithExpression_ChangesOnlyTheTargetedProperty()
	{
		var changed = Frame with { Time = TimeSpan.FromMilliseconds(2_500) };

		Assert.Equal(TimeSpan.FromMilliseconds(2_500), changed.Time);
		Assert.Equal(TimeSpan.FromMilliseconds(1_000), Frame.Time);
		Assert.Equal(2, changed.State.HitCounts.Num300);
		Assert.Equal(3, changed.State.HitCounts.Num100);
		Assert.Equal(4, changed.State.HitCounts.Num50);
		Assert.Equal(5, changed.State.HitCounts.NumGeki);
		Assert.Equal(6, changed.State.HitCounts.NumKatu);
		Assert.Equal(7, changed.State.HitCounts.NumMiss);
		Assert.Equal(287_454_020, changed.State.TotalScore);
		Assert.Equal(300, changed.State.MaxCombo);
		Assert.Equal(11, changed.State.CurrentCombo);
		Assert.False(changed.State.IsFullCombo);
		Assert.Equal(142, changed.Health);
		Assert.Equal(9, changed.TagByte);
		Assert.Equal(new ScoreV2Portion(0.5, 0.25), changed.Sv2Portion);
	}
}