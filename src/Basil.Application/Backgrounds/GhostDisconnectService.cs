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
///     the interval the osu! client itself is used to ping the server. Cleanup is delegated to
///     <see cref="PlayerLogoutService" />, the same service a graceful logout packet uses.
/// </remarks>
/// <param name="gameRegistry">The registry of live <see cref="GameSession" /> sessions this watchdog scans.</param>
/// <param name="ircRegistry">The registry of live <see cref="IrcSession" /> sessions this watchdog scans.</param>
/// <param name="playerLogout">The logout service that performs the actual session cleanup.</param>
/// <param name="logger">The logger used to record a reap that failed without aborting the sweep.</param>
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
	/// <remarks>
	///     One session's reap failing (e.g. a stuck match lock or a transient error) is logged and
	///     skipped rather than propagated, so it can neither abort the rest of the sweep nor, via the
	///     host's default background-service exception behavior, take down the whole process.
	/// </remarks>
	/// <param name="cancellationToken">A token that cooperatively cancels the operation.</param>
	/// <returns>A task that completes when the pass has finished.</returns>
	public async Task RunOnce(CancellationToken cancellationToken = default)
	{
		var currentTime = DateTimeOffset.UtcNow;
		var sessions = gameRegistry.All.Concat<UserSession>(ircRegistry.All);

		foreach (var player in sessions)
		{
			if (player.IsBot || currentTime - player.LastRecvTime <=
			    TimeSpan.FromSeconds(OsuClientMinPingIntervalSeconds))
				continue;

			try
			{
				await playerLogout.LogoutAsync(player, cancellationToken);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				logger.LogError(ex, "Ghost-disconnect reap failed: UserId={UserId} Username={Username}",
					player.Id, player.Name);
			}
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