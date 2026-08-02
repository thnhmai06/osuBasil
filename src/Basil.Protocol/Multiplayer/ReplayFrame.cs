namespace Basil.Protocol.Multiplayer;

/// <summary>
///     osu!'s mouse/keyboard button-state bitfield for a replay frame. Values match
///     osu.Game.Replays.Legacy.ReplayButtonState (Left1/Right1 are the two mouse buttons, Left2/Right2
///     are the two keyboard keybinds).
/// </summary>
[Flags]
public enum Keys
{
	/// <summary>No buttons pressed.</summary>
	None = 0,

	/// <summary>The left mouse button.</summary>
	Left1 = 1,

	/// <summary>The right mouse button.</summary>
	Right1 = 2,

	/// <summary>The first keyboard keybind.</summary>
	Left2 = 4,

	/// <summary>The second keyboard keybind.</summary>
	Right2 = 8,

	/// <summary>The smoke key.</summary>
	Smoke = 16
}

/// <summary>
///     osu!taiko's replay-frame bitfield, which drum zone(s) were hit this frame plus whether the
///     hit was a "big" (kat/don) or large-double-note press. Values match
///     osu.Game.Rulesets.Taiko.Replays.Legacy's own frame bit layout.
/// </summary>
[Flags]
public enum TaikoByte : byte
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

/// <summary>Represents a single replay frame's button state and cursor position.</summary>
/// <param name="ButtonState">The mouse and keyboard button state for the frame.</param>
/// <param name="TaikoByte">The osu!taiko drum-zone bitfield for the frame.</param>
/// <param name="X">The horizontal cursor coordinate.</param>
/// <param name="Y">The vertical cursor coordinate.</param>
/// <param name="Time">The frame time in milliseconds since the start of the play.</param>
public sealed record ReplayFrame(Keys ButtonState, TaikoByte TaikoByte, float X, float Y, int Time);