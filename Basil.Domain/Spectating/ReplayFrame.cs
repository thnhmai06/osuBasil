using System.Drawing;

namespace Basil.Domain.Spectating;

/// <summary>One sample of the input of a play.</summary>
/// <param name="Keys">The keys held at this moment.</param>
/// <param name="Drum">The taiko drum parts hit at this moment.</param>
/// <param name="Position">The cursor position in playfield coordinates.</param>
/// <param name="Time">The time since the start of the song.</param>
public readonly record struct ReplayFrame(KeyState Keys, TaikoDrumState Drum, PointF Position, TimeSpan Time);

/// <summary>The keys held in a replay frame; the bit values carry the game numbering.</summary>
[Flags]
public enum KeyState : byte
{
	/// <summary>No key is held.</summary>
	None = 0,

	/// <summary>The first mouse button (M1).</summary>
	Left1 = 1 << 0,

	/// <summary>The second mouse button (M2).</summary>
	Right1 = 1 << 1,

	/// <summary>The first keyboard key (K1).</summary>
	Left2 = 1 << 2,

	/// <summary>The second keyboard key (K2).</summary>
	Right2 = 1 << 3,

	/// <summary>The smoke key is held.</summary>
	Smoke = 1 << 4
}

/// <summary>The parts of the taiko drum hit in a replay frame; the bits are passed through as the client sends them and their meanings are not verified.</summary>
[Flags]
public enum TaikoDrumState : byte
{
	/// <summary>Nothing is hit.</summary>
	None = 0,

	/// <summary>The center of the drum (don).</summary>
	Don = 1 << 0,

	/// <summary>The rim of the drum (kat).</summary>
	Kat = 1 << 1,

	/// <summary>The right-hand side of the drum.</summary>
	Right = 1 << 2,

	/// <summary>The hit is a big hit.</summary>
	Big = 1 << 3,

	/// <summary>A big hit made with both sides at once.</summary>
	LargeDouble = 1 << 4
}