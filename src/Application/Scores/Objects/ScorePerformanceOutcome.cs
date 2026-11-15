namespace Basil.Application.Scores.Objects;

/// <summary>What happened to a request to calculate the performance points of stored scores.</summary>
public enum ScorePerformanceOutcome : byte
{
	/// <summary>The caller lacks the privileges the operation needs.</summary>
	Denied = 0,

	/// <summary>The calculation started.</summary>
	Ok = 1,

	/// <summary>A calculation is already running.</summary>
	Busy
}
