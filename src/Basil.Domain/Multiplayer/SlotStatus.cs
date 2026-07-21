namespace Basil.Domain.Multiplayer;

/// <summary>Ported from app/objects/match.py's SlotStatus (IntEnum) — an individual match slot's state.</summary>
[Flags]
public enum SlotStatus : byte
{
    Open = 1 << 0,
    Locked = 1 << 1,
    NotReady = 1 << 2,
    Ready = 1 << 3,
    NoMap = 1 << 4,
    Playing = 1 << 5,
    Complete = 1 << 6,
    Quit = 1 << 7
}