using Basil.Application.Scores.Capabilities;
using Basil.Application.Scores.Services;
using Basil.Application.Shared.Actors;

namespace Basil.Application.Scores.Objects;

/// <summary>The performance points of the stored scores, and their calculation for the scores that lack them.</summary>
public sealed class ScorePerformances
{
	internal ScorePerformances(TimeProvider clock, IScorePerformanceRecalculator recalculator)
	{
		Clock = clock;
		Recalculator = recalculator;
	}

	internal TimeProvider Clock { get; }

	internal IScorePerformanceRecalculator Recalculator { get; }

	/// <summary>Scopes the performance points to an actor and its authority.</summary>
	/// <param name="actor">The actor acting.</param>
	/// <returns>A short-lived scope for the actor.</returns>
	public ScopedScorePerformances ScopeTo(IActor actor)
	{
		return new ScopedScorePerformances(this, actor);
	}

	/// <summary>Starts calculating performance points in the background.</summary>
	/// <param name="scope">Which scores it covers.</param>
	/// <returns>Ok when it started; Busy when a calculation is already running.</returns>
	internal ScorePerformanceOutcome Recalculate(ScorePerformanceScope scope)
	{
		return Recalculator.TryStart(scope) ? ScorePerformanceOutcome.Ok : ScorePerformanceOutcome.Busy;
	}
}
