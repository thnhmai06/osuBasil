using OpenOsuTournament.Bancho.Protocol.Packets;

namespace OpenOsuTournament.Bancho.Protocol.Multiplayer;

/// <summary>
///     Wire-shape for a multiplayer match, as needed by <see cref="ServerPacketWriter" />. This is a
///     pure Protocol-layer DTO (no dependency on the richer Domain match entity) — a mapper in a
///     later phase translates the real match aggregate into this shape before writing.
///     Ported from write_match in app/packets.py.
/// </summary>
public sealed record MatchPacketData(
    int Id,
    bool InProgress,
    int Mods,
    string Name,
    string Password,
    string MapName,
    int MapId,
    string MapMd5,
    IReadOnlyList<MatchSlotData> Slots,
    int HostId,
    int Mode,
    int WinCondition,
    int TeamType,
    bool FreeMods,
    int Seed);

/// <summary>One of a match's 16 slots.</summary>
public sealed record MatchSlotData(int Status, int Team, int Mods, int? PlayerId)
{
    public bool HasPlayer => MatchSlotStatusMask.HasPlayer(Status);
}

/// <summary>
///     The slot-status bitmask bancho.py uses (`status &amp; 0b01111100 != 0`, app/packets.py) to decide
///     whether a slot has an occupying player — shared here so <see cref="MatchSlotData.HasPlayer" />
///     and <see cref="Packets.BanchoPacketReader.ReadMatch" /> don't each reimplement the magic number.
/// </summary>
internal static class MatchSlotStatusMask
{
    private const int HasPlayerMask = 0b0111_1100;

    public static bool HasPlayer(int status) => (status & HasPlayerMask) != 0;
}