using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Core;

/// <summary>
///     Shared packet-assembly helpers reading a PlayerSession's own cached state (geoloc, per-mode
///     stats) — used by both LoginService and packet handlers that need to (re-)send a player's
///     presence/stats. Ported from app/packets.py's user_presence(player)/user_stats(player).
/// </summary>
public static class PacketBuilders
{
    public static byte[] BuildUserPresence(PlayerSession session)
    {
        return ServerPacketWriter.UserPresence(
            session.Id, session.Name, session.UtcOffset, (int)session.Geoloc.Country,
            (int)session.BanchoPriv, (int)session.Status.Mode,
            session.Geoloc.Longitude, session.Geoloc.Latitude, session.CurrentStats?.Rank ?? 0);
    }

    public static byte[] BuildUserStats(PlayerSession session)
    {
        return ServerPacketWriter.UserStats(
            session.Id, (int)session.Status.UserActivity, session.Status.InfoText, session.Status.MapMd5,
            (int)session.Status.Mods, (int)session.Status.Mode, session.Status.MapId,
            session.CurrentStats?.Rscore ?? 0, session.CurrentStats?.Acc ?? 0.0, session.CurrentStats?.Plays ?? 0,
            session.CurrentStats?.Tscore ?? 0, session.CurrentStats?.Rank ?? 0, 0);
    }
}