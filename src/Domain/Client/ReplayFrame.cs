using System.Drawing;

namespace Basil.Domain.Client;

/// <summary>Represents a single replay frame's button state and cursor position.</summary>
/// <param name="Keys">The mouse and keyboard button state for the frame.</param>
/// <param name="Drum">The osu!taiko drum-zone bitfield for the frame.</param>
/// <param name="Position">The cursor position.</param>
/// <param name="Time">The time since the start of the play at which the frame was sampled.</param>
public readonly record struct ReplayFrame(KeyState Keys, TaikoDrumState Drum, PointF Position, TimeSpan Time);

/// <summary>
///     osu!'s mouse/keyboard button-state bitfield for a replay frame: Left1 and Right1 are the two mouse
///     buttons, and Left2 and Right2 are the two keyboard keybinds.
/// </summary>
[Flags]
public enum KeyState : byte
{
	/// <summary>No buttons pressed.</summary>
	None = 0,

	/// <summary>The left mouse button.</summary>
	Left1 = 1 << 0,

	/// <summary>The right mouse button.</summary>
	Right1 = 1 << 1,

	/// <summary>The first keyboard keybind.</summary>
	Left2 = 1 << 2,

	/// <summary>The second keyboard keybind.</summary>
	Right2 = 1 << 3,

	/// <summary>The smoke key.</summary>
	Smoke = 1 << 4
}

/// <summary>
///     osu!taiko's replay-frame bitfield: which drum zone(s) were hit this frame, plus whether the hit was a
///     "big" (kat/don) or large-double-note press.
/// </summary>
[Flags]
public enum TaikoDrumState : byte
{
	/// <summary>No drum zone hit this frame.</summary>
	None = 0,

	/// <summary>The center drum (don) was hit.</summary>
	Don = 1 << 0,

	/// <summary>The rim drum (kat) was hit.</summary>
	Kat = 1 << 1,

	/// <summary>The right drum was hit.</summary>
	Right = 1 << 2,

	/// <summary>The hit was a big note press.</summary>
	Big = 1 << 3,

	/// <summary>The frame is a large double-note press.</summary>
	LargeDouble = 1 << 4
}