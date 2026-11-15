using Basil.Application.Scores.Objects;

namespace Basil.Application.Scores.Services;

/// <summary>
///     Calculates the performance points of stored scores in the background. A score whose beatmap or difficulty
///     file is missing keeps the points it has; running again gives the same result.
/// </summary>
public interface IScorePerformanceRecalculator
{
	/// <summary>Gets a value that indicates whether a calculation is running.</summary>
	bool Running { get; }

	/// <summary>Starts a calculation in the background.</summary>
	/// <param name="scope">Which scores it covers.</param>
	/// <returns><see langword="true" /> when it started; <see langword="false" /> when one is already running.</returns>
	bool TryStart(ScorePerformanceScope scope);
}
