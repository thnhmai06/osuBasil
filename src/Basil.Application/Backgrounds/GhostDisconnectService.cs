using Basil.Application.Sessions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Backgrounds;

/// <summary>
///     Background watchdog that logs out players whose connection has been silent for longer than
///     the osu!-defined client ping interval.
/// </summary>
/// <remarks>
///     Runs a cleanup pass every <c>OsuClientMinPingIntervalSeconds / 3</c> seconds and force-logs-out
///     any userSession whose last received packet is older than the full interval; the threshold mirrors
///     the interval the osu! client itself uses to ping the server. Cleanup is delegated to
///     <see cref="PlayerLogoutService" />, the same service a graceful logout packet uses, rather than
///     being duplicated here. A hand-rolled duplicate previously drifted out of sync: it never called
///     into match-leave, so a ghost's slot stayed stuck at SlotStatus.Playing forever and permanently
///     blocked every other userSession's MatchComplete from completing the round.
/// </remarks>
/// <param name="gameRegistry">The registry of live <see cref="GameSession" /> sessions this watchdog scans.</param>
/// <param name="ircRegistry">The registry of live <see cref="IrcSession" /> sessions this watchdog scans.</param>
/// <param name="playerLogout">The logout service that performs the actual session cleanup.</param>
/// <param name="logger">The logger used to record cleanup failures.</param>
public sealed class GhostDisconnectService(
	ISessionRegistry<GameSession> gameRegistry,
	ISessionRegistry<IrcSession> ircRegistry,
	PlayerLogoutService playerLogout,
	ILogger<GhostDisconnectService> logger) : BackgroundService
{
	private const int OsuClientMinPingIntervalSeconds = 300;
	private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(OsuClientMinPingIntervalSeconds / 3.0);

	/// <summary>
	///     Runs a single cleanup pass over every registered userSession session.
	/// </summary>
	/// <param name="cancellationToken">A token that cooperatively cancels the operation.</param>
	/// <returns>A task that completes when the pass has finished.</returns>
	public async Task RunOnce(CancellationToken cancellationToken = default)
	{
		var currentTime = DateTimeOffset.UtcNow;

		foreach (var player in gameRegistry.All.Concat<UserSession>(ircRegistry.All))
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

	/// <summary>
	///     Runs the periodic cleanup loop until the host requests shutdown.
	/// </summary>
	/// <param name="stoppingToken">A token that is triggered when the host is stopping.</param>
	/// <returns>A task that completes when the loop exits.</returns>
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			await Task.Delay(CheckInterval, stoppingToken);
			await RunOnce(stoppingToken);
		}
	}
}