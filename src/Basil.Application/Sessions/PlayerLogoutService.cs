using Basil.Application.Services.Multiplayer;
using Basil.Application.Services.Spectating;
using Basil.Application.Sessions.Channels;
using Basil.Protocol.Packets;

namespace Basil.Application.Sessions;

/// <summary>
///     Ported from Player.logout, minus the LOGOUT packet's 1-second login-grace-period check (which
///     stays in LogoutHandler since it's specific to that packet, not part of "logout" semantics).
///     Shared with !reconnect, which forces the same cleanup on a session outside of that packet.
/// </summary>
public sealed class PlayerLogoutService(
    IPlayerSessionRegistry sessionRegistry,
    IChannelRegistry channelRegistry,
    SpectatorService spectatorService,
    MatchMembershipService matchMembership)
{
    public void Logout(PlayerSession player)
    {
        if (player.Match is { } match)
        {
            match.Lock.Wait();
            try
            {
                matchMembership.Leave(player, match);
            }
            finally
            {
                match.Lock.Release();
            }
        }

        if (player.Spectating is { } host) spectatorService.RemoveSpectator(host, player);

        foreach (var channelName in player.Channels.ToArray())
        {
            var channel = channelRegistry.GetByName(channelName);
            if (channel is null) continue;

            channel.Part(player.Id);
            player.LeaveChannel(channelName);

            foreach (var session in sessionRegistry.All)
                if (channel.CanRead(session.Priv))
                    session.Enqueue(ServerPacketWriter.ChannelInfo(channel.Name, channel.Topic, channel.PlayerCount));
        }

        sessionRegistry.Remove(player);

        if (!player.Restricted)
            foreach (var other in sessionRegistry.All)
                other.Enqueue(ServerPacketWriter.Logout(player.Id));
    }
}