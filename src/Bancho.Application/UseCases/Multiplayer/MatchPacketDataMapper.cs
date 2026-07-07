using Bancho.Application.Sessions.Multiplayer;
using Bancho.Protocol.Multiplayer;
using Bancho.Protocol.Packets;

namespace Bancho.Application.UseCases.Multiplayer;

/// <summary>
///     Maps the richer <see cref="MatchSession" /> domain/session model onto the flat wire shape
///     <see cref="ServerPacketWriter.WriteMatch" /> needs. The real password is always passed through
///     unmasked — <c>WriteMatch</c>'s own <c>sendPassword</c> flag decides whether it's actually
///     written or replaced with a placeholder, matching write_match in app/packets.py.
/// </summary>
public static class MatchPacketDataMapper
{
    public static MatchPacketData ToPacketData(MatchSession match)
    {
        return new MatchPacketData(
            match.Id,
            match.InProgress,
            (int)match.Mods,
            match.Name,
            match.Password,
            match.MapName,
            match.MapId,
            match.MapMd5,
            [.. match.Slots.Select(s => new MatchSlotData((int)s.Status, (int)s.Team, (int)s.Mods, s.PlayerId))],
            match.HostId,
            (int)match.Mode,
            (int)match.WinCondition,
            (int)match.TeamType,
            match.Freemods,
            match.Seed);
    }
}