using Basil.Application.Sessions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Application.BackgroundServices;

/// <summary>
///     Ported from app/bg_loops.py's _disconnect_ghosts: every OSU_CLIENT_MIN_PING_INTERVAL/3
///     seconds, force-logs-out any player whose last recv time exceeds
///     OSU_CLIENT_MIN_PING_INTERVAL — mirrors the (osu!-defined) client ping interval, not a value
///     bancho.py invented. Delegates the actual cleanup to <see cref="PlayerLogoutService" /> (the
///     same method a graceful LOGOUT packet uses) rather than hand-rolling a second copy — bancho.py's
///     own _disconnect_ghosts does the same, just calling player.logout(). A hand-rolled duplicate
///     here previously drifted out of sync: it never called into match-leave, so a ghost's slot stayed
///     stuck at SlotStatus.Playing forever, permanently blocking every other player's MatchComplete
///     from ever completing the round ("Waiting for other players to finish..." with no way out short
///     of a manual !mp kick).
/// </summary>
public sealed class GhostDisconnectService(
    IPlayerSessionRegistry sessionRegistry,
    PlayerLogoutService playerLogout,
    ILogger<GhostDisconnectService> logger) : BackgroundService
{
    private const int OsuClientMinPingIntervalSeconds = 300;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(OsuClientMinPingIntervalSeconds / 3.0);

    public async Task RunOnce(CancellationToken cancellationToken = default)
    {
        var currentTime = DateTimeOffset.UtcNow;

        foreach (var player in sessionRegistry.All)
            if (!player.IsBot && currentTime - player.LastRecvTime >
                TimeSpan.FromSeconds(OsuClientMinPingIntervalSeconds))
                try
                {
                    await playerLogout.LogoutAsync(player, cancellationToken);
                }
                catch (Exception e)
                {
                    // This watchdog is the only path that ever reaps a dead connection — one throw
                    // must never kill the loop and silently stop reaping every future ghost too.
                    logger.LogError(e, "Ghost-disconnect cleanup failed: UserId={UserId}", player.Id);
                }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(CheckInterval, stoppingToken);
            await RunOnce(stoppingToken);
        }
    }
}
