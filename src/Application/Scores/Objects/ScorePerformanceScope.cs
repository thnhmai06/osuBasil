namespace Basil.Application.Scores.Objects;

/// <summary>Which stored scores a calculation of performance points covers.</summary>
public enum ScorePerformanceScope : byte
{
	/// <summary>Only the passed scores whose performance points are not calculated.</summary>
	Missing,

	/// <summary>Every passed score, replacing the performance points it has.</summary>
	All
}
