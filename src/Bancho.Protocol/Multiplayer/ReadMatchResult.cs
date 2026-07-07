using Bancho.Protocol.Packets;
namespace Bancho.Protocol.Multiplayer;

/// <summary>
/// Ported from MultiplayerMatch (dataclass) in app/packets.py, as produced by
/// <see cref="BanchoPacketReader.ReadMatch"/>. Note this is intentionally a different shape than
/// <see cref="MatchPacketData"/> (the write side) — the wire format itself is asymmetric: reading
/// yields a flat <see cref="SlotIds"/> list (only for occupied slots), not a full per-slot
/// player/mods mapping.
/// </summary>
public sealed record ReadMatchResult(
    int Id,
    bool InProgress,
    int Powerplay,
    int Mods,
    string Name,
    string Password,
    string MapName,
    int MapId,
    string MapMd5,
    IReadOnlyList<int> SlotStatuses,
    IReadOnlyList<int> SlotTeams,
    IReadOnlyList<int> SlotIds,
    int HostId,
    int Mode,
    int WinCondition,
    int TeamType,
    bool FreeMods,
    IReadOnlyList<int> SlotMods,
    int Seed);
