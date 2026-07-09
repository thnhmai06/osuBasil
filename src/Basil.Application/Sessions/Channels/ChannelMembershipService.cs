using Basil.Protocol.Irc;
using Basil.Protocol.Packets;

namespace Basil.Application.Sessions.Channels;

/// <summary>
///     Ported from Player.join_channel/leave_channel — the shared membership logic bancho.py reuses
///     for both client-initiated CHANNEL_JOIN/CHANNEL_PART packets and server-initiated instance
///     membership (spectator, later multiplayer). Broadcast scope differs by channel kind: an
///     instance channel only notifies its own current members; an ordinary channel notifies every
///     session that can read it.
///     Also owns the IRC-shaped JOIN/PART/QUIT/PRIVMSG broadcast primitives — kept dependency-free of
///     <c>ICommandDispatcher</c> on purpose (CommandDispatcher -&gt; MpCommandService -&gt;
///     MatchMembershipService -&gt; this class already forms a chain; adding the reverse edge here would
///     be a DI cycle). Command dispatch lives one layer up, in <c>ChatDispatchService</c>.
/// </summary>
public sealed class ChannelMembershipService(IPlayerSessionRegistry sessionRegistry, IChannelRegistry channelRegistry)
{
    public bool Join(PlayerSession player, ChannelSession channel)
    {
        if (player.InChannel(channel.Name) || !channel.CanRead(player.Priv)) return false;

        channel.Join(player.Id);
        player.JoinChannel(channel.Name);
        player.Enqueue(ServerPacketWriter.ChannelJoin(channel.DisplayName));

        BroadcastChannelInfo(channel);

        var joinMessage = IrcMessageWriter.Join(player.Name, player.Id, channel.Name);
        foreach (var memberId in channel.MemberIds)
            sessionRegistry.GetById(memberId)?.IrcConnection.Send(joinMessage);

        return true;
    }

    public void Part(PlayerSession player, ChannelSession channel, bool kick = true)
    {
        if (!player.InChannel(channel.Name)) return;

        var partMessage = IrcMessageWriter.Part(player.Name, player.Id, channel.Name);
        foreach (var memberId in channel.MemberIds)
            sessionRegistry.GetById(memberId)?.IrcConnection.Send(partMessage);

        channel.Part(player.Id);
        player.LeaveChannel(channel.Name);

        if (kick) player.Enqueue(ServerPacketWriter.ChannelKick(channel.DisplayName));

        BroadcastChannelInfo(channel);
    }

    /// <summary>
    ///     Cleans up every channel <paramref name="player" /> is in and notifies the remaining IRC-shaped
    ///     connections with a single QUIT each (deduped across shared channels) — called when a real IRC
    ///     TCP connection disconnects. Bancho sessions never call this (they leave via GhostDisconnectService,
    ///     which doesn't need a QUIT broadcast since bancho clients only ever saw ChannelInfo counts).
    /// </summary>
    public void Quit(PlayerSession player, string reason)
    {
        var quitMessage = IrcMessageWriter.Quit(player.Name, player.Id, reason);
        var notified = new HashSet<int>();

        foreach (var channelName in player.Channels.ToList())
        {
            var channel = channelRegistry.GetByName(channelName);
            if (channel is null) continue;

            foreach (var memberId in channel.MemberIds)
                if (memberId != player.Id && notified.Add(memberId))
                    sessionRegistry.GetById(memberId)?.IrcConnection.Send(quitMessage);

            channel.Part(player.Id);
            player.LeaveChannel(channel.Name);
            BroadcastChannelInfo(channel);
        }
    }

    /// <summary>
    ///     Ported from Channel.enqueue — sends raw packet bytes to every session currently in the
    ///     channel (not everyone who merely *can read* it), optionally skipping an immune set. Used
    ///     by multiplayer's match.enqueue/enqueue_state, which routes through the match's chat
    ///     channel exactly like the Python source.
    /// </summary>
    public void BroadcastToMembers(ChannelSession channel, byte[] packet, IReadOnlyCollection<int>? immune = null)
    {
        foreach (var memberId in channel.MemberIds)
        {
            if (immune is not null && immune.Contains(memberId)) continue;

            sessionRegistry.GetById(memberId)?.Enqueue(packet);
        }
    }

    /// <summary>
    ///     IRC-shaped counterpart of <see cref="BroadcastToMembers" /> for chat text specifically —
    ///     routes through each member's <see cref="Sessions.Irc.IIrcConnection" /> instead of a raw
    ///     bancho packet, so it reaches real IRC clients and bancho clients alike.
    /// </summary>
    public void BroadcastPrivmsg(ChannelSession channel, IrcMessage message, int? skipMemberId = null)
    {
        foreach (var memberId in channel.MemberIds)
        {
            if (memberId == skipMemberId) continue;

            sessionRegistry.GetById(memberId)?.IrcConnection.Send(message);
        }
    }

    private void BroadcastChannelInfo(ChannelSession channel)
    {
        var packet = ServerPacketWriter.ChannelInfo(channel.DisplayName, channel.Topic, channel.PlayerCount);

        if (channel.Instance)
            foreach (var memberId in channel.MemberIds)
                sessionRegistry.GetById(memberId)?.Enqueue(packet);
        else
            foreach (var session in sessionRegistry.All)
                if (channel.CanRead(session.Priv))
                    session.Enqueue(packet);
    }
}