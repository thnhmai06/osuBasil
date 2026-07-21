using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;

namespace Basil.Application.Sessions.Multiplayer;

/// <summary>
///     One of a match's 16 slots. Ported from app/objects/match.py's Slot — a plain mutable holder,
///     synchronisation is the owning <see cref="MatchSession" />'s responsibility (its <c>Lock</c>),
///     not this type's.
/// </summary>
public sealed class MatchSlot
{
    public int? PlayerId { get; set; }
    public SlotStatus Status { get; set; } = SlotStatus.Open;
    public MatchTeam Team { get; set; } = MatchTeam.Neutral;
    public Mods Mods { get; set; } = Mods.NoMod;
    public bool Loaded { get; set; }
    public bool Skipped { get; set; }

    public bool Empty => PlayerId is null;

    public void CopyFrom(MatchSlot other)
    {
        PlayerId = other.PlayerId;
        Status = other.Status;
        Team = other.Team;
        Mods = other.Mods;
    }

    public void Reset(SlotStatus newStatus = SlotStatus.Open)
    {
        PlayerId = null;
        Status = newStatus;
        Team = MatchTeam.Neutral;
        Mods = Mods.NoMod;
        Loaded = false;
        Skipped = false;
    }
}