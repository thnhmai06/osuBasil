using Bancho.Application.Sessions;
using Bancho.Protocol;
using Bancho.Application.Sessions.Multiplayer;
using Bancho.Protocol.Multiplayer;
using Bancho.Protocol.Packets;

namespace Bancho.Application.UseCases.Multiplayer;

/// <summary>
/// Maps the richer <see cref="MatchSession"/> domain/session model onto the flat wire shape
/// <see cref="ServerPacketWriter.WriteMatch"/> needs. The real password is always passed through
/// unmasked — <c>WriteMatch</c>'s own <c>sendPassword</c> flag decides whether it's actually
/// written or replaced with a placeholder, matching write_match in app/packets.py.
/// </summary>
public static class MatchPacketDataMapper
{
    public static MatchPacketData ToPacketData(MatchSession match) => new(
        Id: match.Id,
        InProgress: match.InProgress,
        Mods: (int)match.Mods,
        Name: match.Name,
        Password: match.Password,
        MapName: match.MapName,
        MapId: match.MapId,
        MapMd5: match.MapMd5,
        Slots: [.. match.Slots.Select(s => new MatchSlotData((int)s.Status, (int)s.Team, (int)s.Mods, s.PlayerId))],
        HostId: match.HostId,
        Mode: (int)match.Mode,
        WinCondition: (int)match.WinCondition,
        TeamType: (int)match.TeamType,
        FreeMods: match.Freemods,
        Seed: match.Seed);
}
