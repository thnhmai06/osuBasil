namespace Bancho.Protocol.Multiplayer;

/// <summary>Ported from ReplayFrame (NamedTuple) in app/packets.py.</summary>
public sealed record ReplayFrameData(int ButtonState, int TaikoByte, float X, float Y, int Time);