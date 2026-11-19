namespace Basil.Application.Scores.Objects;

public enum ScoreRecalculationResult : byte
{
	Denied = 0,

	Ok = 1,

	Busy
}