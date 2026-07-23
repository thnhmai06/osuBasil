using Basil.Application.Services.Bot;
using Basil.Application.Services.Spectating;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Hosting;

namespace Basil.Application.BackgroundServices;

/// <summary>
///     Ported from app/bg_loops.py's _disconnect_ghosts: every OSU_CLIENT_MIN_PING_INTERVAL/3
///     seconds, force-logs-out any player whose last recv time exceeds
///     OSU_CLIENT_MIN_PING_INTERVAL — mirrors the (osu!-defined) client ping interval, not a value
///     bancho.py invented.
/// </summary>
public sealed class GhostDisconnectService(
    IPlayerSessionRegistry sessionRegistry,
    ChannelMembershipService channelMembership,
    SpectatorService spectatorService) : BackgroundService
{
    private const int OsuClientMinPingIntervalSeconds = 300;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(OsuClientMinPingIntervalSeconds / 3.0);

    public void RunOnce()
    {
        var currentTime = DateTimeOffset.UtcNow;

        foreach (var player in sessionRegistry.All)
            if (!player.IsBot && currentTime - player.LastRecvTime > TimeSpan.FromSeconds(OsuClientMinPingIntervalSeconds))
            {
                // Parts every joined channel (broadcasting IRC QUIT to real IRC clients still in
                // them) before dropping the session — otherwise a ghosted IRC member would linger
                // in ChannelSession.MemberIds/NAMES forever, and PlayerCount would stay wrong.
                channelMembership.Quit(player, "Ping timeout");

                // #spec_{userId} is keyed by the persistent user id, stable across relogins — tear
                // down BasilBot's watch of this ghosted player now, matching PlayerLogoutService's
                // explicit-logout cleanup, or the channel would keep a dead member reference.
                var bot = sessionRegistry.GetById(BotBootstrapService.BotId);
                if (bot is not null) spectatorService.RemoveSpectator(player, bot);

                sessionRegistry.Remove(player);

                if (!player.Restricted)
                    foreach (var other in sessionRegistry.All)
                        other.Enqueue(ServerPacketWriter.Logout(player.Id));
            }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(CheckInterval, stoppingToken);
            RunOnce();
        }
    }
}