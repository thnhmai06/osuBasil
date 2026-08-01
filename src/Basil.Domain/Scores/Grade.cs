namespace Basil.Domain.Scores;

/// <summary>
///     Represents the grade of a score.
/// </summary>
/// <remarks>
///     Ordered so that higher grades compare as greater values, which reads naturally in
///     comparisons; XH is the highest grade. This is the opposite numeric order from osu!'s own
///     grade ordering.
/// </remarks>
public enum Grade : byte
{
	/// <summary>No grade, used for failed or incomplete plays.</summary>
	N = 0,

	/// <summary>Fail grade, awarded when the play fails.</summary>
	F = 1,

	/// <summary>D grade.</summary>
	D = 2,

	/// <summary>C grade.</summary>
	C = 3,

	/// <summary>B grade.</summary>
	B = 4,

	/// <summary>A grade.</summary>
	A = 5,

	/// <summary>S grade.</summary>
	S = 6,

	/// <summary>S grade achieved with the Hidden mod.</summary>
	Sh = 7,

	/// <summary>SS grade, achieved with all 300 judgments.</summary>
	X = 8,

	/// <summary>SS grade achieved with the Hidden mod.</summary>
	Xh = 9
}