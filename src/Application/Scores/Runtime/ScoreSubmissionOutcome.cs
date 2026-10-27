namespace Basil.Application.Scores.Runtime;

/// <summary>What became of a score submission that was allowed to run.</summary>
public enum ScoreSubmissionOutcome : byte
{
	/// <summary>The score was stored.</summary>
	Ok = 1,

	/// <summary>The submission was rejected; the rejection says why.</summary>
	Rejected
}