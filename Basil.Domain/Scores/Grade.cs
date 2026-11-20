namespace Basil.Domain.Scores;

/// <summary>The letter rating of a play; the values are Basil's own ordering from worst to best, not the client's numbering.</summary>
public enum Grade : byte
{
	/// <summary>No grade: the play has not been rated.</summary>
	N = 0,

	/// <summary>Failed: the play was not completed.</summary>
	F = 1,

	/// <summary>Grade D.</summary>
	D = 2,

	/// <summary>Grade C.</summary>
	C = 3,

	/// <summary>Grade B.</summary>
	B = 4,

	/// <summary>Grade A.</summary>
	A = 5,

	/// <summary>Grade S: nearly all hits are perfect.</summary>
	S = 6,

	/// <summary>Grade S in silver, earned with a mod that hides the playfield (Hidden, Flashlight or Fade In).</summary>
	Sh = 7,

	/// <summary>Grade SS: every hit is perfect.</summary>
	X = 8,

	/// <summary>Grade SS in silver, earned with a mod that hides the playfield (Hidden, Flashlight or Fade In).</summary>
	Xh = 9
}