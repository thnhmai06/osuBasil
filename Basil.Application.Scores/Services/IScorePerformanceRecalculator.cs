using Basil.Application.Scores.Objects;

namespace Basil.Application.Scores.Services;

public interface IScorePerformanceRecalculator
{
	bool Running { get; }

	bool TryStart(ScorePerformanceScope scope);
}