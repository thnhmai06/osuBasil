using Basil.Application.Sessions;
using Basil.Domain.Beatmaps;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Core;

/// <summary>
///     Shared packet-assembly helpers reading a PlayerSession's own cached state (geoloc, per-mode
///     stats) — used by both OsuLoginUseCase and packet handlers that need to (re-)send a player's
///     presence/stats. Ported from app/packets.py's user_presence(player)/user_stats(player).
/// </summary>
public static class PacketBuilders
{
    public static byte[] BuildUserPresence(PlayerSession session)
    {
        return ServerPacketWriter.UserPresence(
            session.Id, session.Name, session.UtcOffset, session.Geoloc.CountryNumeric,
            (int)session.BanchoPriv, session.Status.Mode.AsVanilla(),
            session.Geoloc.Longitude, session.Geoloc.Latitude, session.CurrentStats?.Rank ?? 0);
    }

    public static byte[] BuildUserStats(PlayerSession session)
    {
        return ServerPacketWriter.UserStats(
            session.Id, (int)session.Status.Action, session.Status.InfoText, session.Status.MapMd5,
            (int)session.Status.Mods, session.Status.Mode.AsVanilla(), session.Status.MapId,
            session.CurrentStats?.Rscore ?? 0, session.CurrentStats?.Acc ?? 0.0, session.CurrentStats?.Plays ?? 0,
            session.CurrentStats?.Tscore ?? 0, session.CurrentStats?.Rank ?? 0, 0);
    }
}