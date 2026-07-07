namespace OpenOsuTournament.Bancho.Protocol.Multiplayer;

/// <summary>
///     osu!'s mouse/keyboard button-state bitfield for a replay frame. Values match
///     osu.Game.Replays.Legacy.ReplayButtonState (Left1/Right1 are the two mouse buttons, Left2/Right2
///     are the two keyboard keybinds).
/// </summary>
[Flags]
public enum Keys
{
    None = 0,
    Left1 = 1,
    Right1 = 2,
    Left2 = 4,
    Right2 = 8,
    Smoke = 16
}

/// <summary>Ported from ReplayFrame (NamedTuple) in app/packets.py.</summary>
public sealed record ReplayFrameData(Keys ButtonState, int TaikoByte, float X, float Y, int Time);