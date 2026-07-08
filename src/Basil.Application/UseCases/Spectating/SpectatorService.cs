using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Protocol.Packets;

namespace Basil.Application.UseCases.Spectating;

/// <summary>
///     Ported from Player.add_spectator/remove_spectator. Each spectated player gets a dedicated
///     `#spec_{hostId}` instance channel (client-visible name always "#spectator", regardless of
///     which host's instance a given client is currently in — see ChannelSession's doc comment),
///     created on the first spectator and torn down once the last one leaves.
///     One simplification versus the Python source: bancho.py's add_spectator/remove_spectator each
///     send an extra, fully redundant channel_info broadcast on top of the one Player.join_channel/
///     leave_channel already sends internally (harmless duplicate packets, tolerated by the client).
///     This port relies on ChannelMembershipService's single broadcast and layers only the
///     spectator-specific notifications (spectator_joined/fellow_spectator_joined/etc.) on top.
/// </summary>
public sealed class SpectatorService(IChannelRegistry channelRegistry, ChannelMembershipService channelMembership)
{
    private static string ChannelNameFor(int hostId)
    {
        return $"#spec_{hostId}";
    }

    public void AddSpectator(PlayerSession host, PlayerSession spectator)
    {
        var channel = channelRegistry.GetByName(ChannelNameFor(host.Id));
        if (channel is null)
        {
            channel = new ChannelSession(
                0, ChannelNameFor(host.Id), $"{host.Name}'s spectator channel.",
                0, 0, false, "#spectator", true);
            channelRegistry.Add(channel);
            channelMembership.Join(host, channel);
        }

        if (!channelMembership.Join(spectator, channel)) return;

        if (!spectator.Stealth)
        {
            var joinedBySpectator = ServerPacketWriter.FellowSpectatorJoined(spectator.Id);
            foreach (var existing in host.Spectators)
            {
                existing.Enqueue(joinedBySpectator);
                spectator.Enqueue(ServerPacketWriter.FellowSpectatorJoined(existing.Id));
            }

            host.Enqueue(ServerPacketWriter.SpectatorJoined(spectator.Id));
        }
        else
        {
            // Stealth: only give the (admin) spectator visibility into existing spectators, not
            // vice-versa — the host and other spectators are never told this player joined.
            foreach (var existing in host.Spectators)
                spectator.Enqueue(ServerPacketWriter.FellowSpectatorJoined(existing.Id));
        }

        host.AddSpectator(spectator);
        spectator.Spectating = host;
    }

    public void RemoveSpectator(PlayerSession host, PlayerSession spectator)
    {
        host.RemoveSpectator(spectator);
        spectator.Spectating = null;

        var channel = channelRegistry.GetByName(ChannelNameFor(host.Id));
        if (channel is null) return;

        channelMembership.Part(spectator, channel);

        if (host.Spectators.Count == 0)
        {
            channelMembership.Part(host, channel);
            channelRegistry.Remove(channel.Name);
            return;
        }

        var fellowLeft = ServerPacketWriter.FellowSpectatorLeft(spectator.Id);
        foreach (var remaining in host.Spectators) remaining.Enqueue(fellowLeft);
    }
}