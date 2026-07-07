namespace Bancho.Domain.Multiplayer;

/// <summary>Ported from app/objects/match.py's SlotStatus (IntEnum) — an individual match slot's state.</summary>
[Flags]
public enum SlotStatus
{
    Open = 1,
    Locked = 2,
    NotReady = 4,
    Ready = 8,
    NoMap = 16,
    Playing = 32,
    Complete = 64,
    Quit = 128,
}
