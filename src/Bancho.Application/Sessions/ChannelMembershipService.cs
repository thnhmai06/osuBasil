using Bancho.Protocol;

namespace Bancho.Application.Sessions;

/// <summary>
/// Ported from Player.join_channel/leave_channel — the shared membership logic bancho.py reuses
/// for both client-initiated CHANNEL_JOIN/CHANNEL_PART packets and server-initiated instance
/// membership (spectator, later multiplayer). Broadcast scope differs by channel kind: an
/// instance channel only notifies its own current members; an ordinary channel notifies every
/// session that can read it.
/// </summary>
public sealed class ChannelMembershipService(IPlayerSessionRegistry sessionRegistry)
{
    public bool Join(PlayerSession player, ChannelSession channel)
    {
        if (player.InChannel(channel.Name) || !channel.CanRead(player.Priv))
        {
            return false;
        }

        channel.Join(player.Id);
        player.JoinChannel(channel.Name);
        player.Enqueue(ServerPacketWriter.ChannelJoin(channel.DisplayName));

        BroadcastChannelInfo(channel);
        return true;
    }

    public void Part(PlayerSession player, ChannelSession channel, bool kick = true)
    {
        if (!player.InChannel(channel.Name))
        {
            return;
        }

        channel.Part(player.Id);
        player.LeaveChannel(channel.Name);

        if (kick)
        {
            player.Enqueue(ServerPacketWriter.ChannelKick(channel.DisplayName));
        }

        BroadcastChannelInfo(channel);
    }

    private void BroadcastChannelInfo(ChannelSession channel)
    {
        var packet = ServerPacketWriter.ChannelInfo(channel.DisplayName, channel.Topic, channel.PlayerCount);

        if (channel.Instance)
        {
            foreach (var memberId in channel.MemberIds)
            {
                sessionRegistry.GetById(memberId)?.Enqueue(packet);
            }
        }
        else
        {
            foreach (var session in sessionRegistry.All)
            {
                if (channel.CanRead(session.Priv))
                {
                    session.Enqueue(packet);
                }
            }
        }
    }
}
