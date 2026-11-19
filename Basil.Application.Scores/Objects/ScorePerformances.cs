using Basil.Application.Scores.Capabilities;
using Basil.Application.Scores.Services;
using Basil.Application.Shared.Actors;

namespace Basil.Application.Scores.Objects;

public sealed class ScorePerformances
{
	internal ScorePerformances(TimeProvider clock, IScorePerformanceRecalculator recalculator)
	{
		Clock = clock;
		Recalculator = recalculator;
	}

	internal TimeProvider Clock { get; }

	internal IScorePerformanceRecalculator Recalculator { get; }

	public ScopedScorePerformances ScopeTo(IActor actor)
	{
		return new ScopedScorePerformances(this, actor);
	}

	internal ScoreRecalculationResult Recalculate(ScorePerformanceScope scope)
	{
		return Recalculator.TryStart(scope) ? ScoreRecalculationResult.Ok : ScoreRecalculationResult.Busy;
	}
}