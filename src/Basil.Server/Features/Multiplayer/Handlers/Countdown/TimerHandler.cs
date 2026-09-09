using Basil.Server.Features.Bot;
using Basil.Server.Shared.Sessions;
using Microsoft.Extensions.Logging;

namespace Basil.Server.Features.Multiplayer.Handlers.Countdown;

/// <summary>Runs a match's countdowns, both the plain <c>!mp timer</c> kind and the auto-start kind queued by <c>!mp start</c>.</summary>
public sealed class TimerHandler(
	MatchLifecycle matchLifecycle,
	MatchBroadcast matchBroadcast,
	ISessionRegistry<GameSession> gameRegistry,
	ILogger<TimerHandler> logger)
{
	private const int PeriodicReminderIntervalSeconds = 60;
	private const int NearTotalIgnoreWindowSeconds = 5;

	/// <summary>
	///     Announcement seconds for a <c>!mp start</c> countdown, ticking down to 3 before going silent until
	///     "Good luck, have fun!"
	/// </summary>
	private static readonly int[] StartCheckpoints = [60, 30, 10, 5, 4, 3];

	/// <summary>Announcement seconds for a <c>!mp timer</c> countdown, which skips the fast final tick entirely.</summary>
	private static readonly int[] TimerOnlyCheckpoints = [60, 30, 10, 5];

	/// <summary>Starts a plain countdown that announces but never auto-starts the match when it finishes.</summary>
	/// <param name="match">The match whose timer to start.</param>
	/// <param name="seconds">The countdown length in seconds.</param>
	/// <param name="mutation">The open mutation scope that publishes the resulting timer.</param>
	public void Timer(MatchSession match, int seconds, MatchMutationScope mutation)
	{
		logger.LogDebug("Timer started: MatchId={MatchId} Seconds={Seconds}", match.DbId, seconds);
		BeginCountdown(match, seconds, false, mutation);
	}

	/// <summary>Computes the descending list of seconds at which a countdown announces.</summary>
	/// <remarks>
	///     Comprises the fixed marks (which depend on <paramref name="autoStart" />) plus an extra
	///     reminder every 60 seconds for long countdowns (for example, a 5-minute timer also
	///     announces at 240, 180, and 120). A mark that is a multiple of 60, whether from the fixed
	///     list's own 60 or a periodic one, is dropped if it falls within
	///     <see cref="NearTotalIgnoreWindowSeconds" /> of <paramref name="totalSeconds" />; otherwise
	///     it would fire almost immediately after "Queued...", which is redundant. The sub-60 marks
	///     (30/10/5/4/3/2/1) are exempt from that check, since they are meant to fire close together
	///     as the final countdown ticks down.
	/// </remarks>
	/// <param name="totalSeconds">The total countdown length in seconds.</param>
	/// <param name="autoStart">
	///     <see langword="true" /> for a <c>!mp start</c> countdown; otherwise, <see langword="false" />
	///     for a plain <c>!mp timer</c>.
	/// </param>
	/// <returns>The checkpoint seconds in descending order.</returns>
	public static IReadOnlyList<int> ComputeAnnounceCheckpoints(int totalSeconds, bool autoStart = true)
	{
		var periodic = Enumerable.Range(1, int.MaxValue)
			.Select(k => k * PeriodicReminderIntervalSeconds)
			.TakeWhile(c => c < totalSeconds);
		var baseCheckpoints = autoStart ? StartCheckpoints : TimerOnlyCheckpoints;

		return
		[
			.. baseCheckpoints
				.Concat(periodic)
				.Where(c => c < totalSeconds)
				.Distinct()
				.Where(c => c % PeriodicReminderIntervalSeconds != 0 || totalSeconds - c > NearTotalIgnoreWindowSeconds)
				.OrderByDescending(c => c)
		];
	}

	/// <summary>Starts a fire-and-forget countdown, cancelling any timer already pending on the match.</summary>
	/// <remarks>
	///     The loop itself (<see cref="CountdownLoopAsync" />) only holds <see cref="MatchSession.Lock" />
	///     briefly at its final tick, never across a <c>Task.Delay</c>, matching this codebase's rule
	///     against holding the lock across an unrelated await.
	/// </remarks>
	/// <param name="match">The match whose countdown to start.</param>
	/// <param name="totalSeconds">The countdown length in seconds.</param>
	/// <param name="autoStart">
	///     <see langword="true" /> to start the match when the countdown finishes; otherwise,
	///     <see langword="false" />.
	/// </param>
	/// <param name="mutation">The open mutation scope that publishes the resulting timer.</param>
	internal void BeginCountdown(MatchSession match, int totalSeconds, bool autoStart, MatchMutationScope mutation)
	{
		CancelPendingTimer(match, announce: true);

		var cts = new CancellationTokenSource();
		match.PendingTimer = cts;
		match.PendingTimerIsAutoStart = autoStart;
		match.TimerStartedAt = DateTimeOffset.UtcNow;
		match.TimerTotalSeconds = totalSeconds;
		mutation.PublishTimer();
		logger.LogDebug("Countdown queued: MatchId={MatchId} Seconds={Seconds} AutoStart={AutoStart}",
			match.DbId, totalSeconds, autoStart);

		// Cuts the countdown loop's AsyncLocal inheritance (RequestId/etc.) from the request that
		// triggered it: the loop can run up to `totalSeconds` after that request has ended, so it
		// must not carry a now-stale RequestId. It pushes its own MatchId scope below instead.
		using (ExecutionContext.SuppressFlow())
		{
			_ = CountdownLoopAsync(match, totalSeconds, autoStart, cts);
		}
	}

	/// <summary>
	///     Cancels the match's pending countdown/timer, if any, clearing its timer state.
	/// </summary>
	/// <remarks>
	///     Does not itself publish the cleared timer state: <see cref="BeginCountdown" /> publishes
	///     the replacement timer right after calling this, and an immediate start publishes
	///     separately, so publishing here too would just be a redundant extra broadcast.
	/// </remarks>
	/// <param name="match">The match whose pending countdown to cancel.</param>
	/// <param name="announce">
	///     <see langword="true" /> to post a chat message reporting how many seconds remained on the
	///     cancelled countdown; otherwise, <see langword="false" /> to cancel silently.
	/// </param>
	/// <returns>
	///     <see langword="true" /> if a countdown was actually pending and got cancelled; otherwise,
	///     <see langword="false" />.
	/// </returns>
	internal bool CancelPendingTimer(MatchSession match, bool announce)
	{
		if (match.PendingTimer is not { } pending) return false;

		pending.Cancel();
		var remaining = match.TimerTotalSeconds is { } total && match.TimerStartedAt is { } startedAt
			? Math.Max(0, total - (int)(DateTimeOffset.UtcNow - startedAt).TotalSeconds)
			: 0;

		match.PendingTimer = null;
		match.PendingTimerIsAutoStart = false;
		match.TimerStartedAt = null;
		match.TimerTotalSeconds = null;

		if (announce)
			Announce(match, $"Cancelled the previous countdown ({remaining} seconds remaining).");

		return true;
	}

	/// <summary>Runs a countdown, announcing at each checkpoint and optionally starting the match at zero.</summary>
	/// <param name="match">The match the countdown belongs to.</param>
	/// <param name="totalSeconds">The total countdown length in seconds.</param>
	/// <param name="autoStart">
	///     <see langword="true" /> to start the match when the countdown finishes; otherwise,
	///     <see langword="false" />.
	/// </param>
	/// <param name="cts">The token source that owns the countdown's cancellation token.</param>
	private async Task CountdownLoopAsync(MatchSession match, int totalSeconds, bool autoStart,
		CancellationTokenSource cts)
	{
		using var _ = logger.BeginScope(new Dictionary<string, object> { ["MatchId"] = match.DbId });

		var token = cts.Token;

		// This loop is started fire-and-forget (see BeginCountdown); an exception here would
		// otherwise fault a Task nobody observes, silently losing it instead of reaching the logs.
		try
		{
			Announce(match,
				autoStart
					? $"Queued the match to start in {totalSeconds} seconds"
					: $"Started a {totalSeconds}-second countdown.");

			var remaining = totalSeconds;
			foreach (var checkpoint in ComputeAnnounceCheckpoints(totalSeconds, autoStart))
			{
				if (!await DelayAsync(remaining - checkpoint, token)) return;

				Announce(match, autoStart
					? $"Match starts in {checkpoint} seconds"
					: $"{checkpoint} seconds remaining");
				// Each tick takes the match lock only long enough to allocate its version. The
				// version used to be allocated out here with no lock at all, which let a tick
				// broadcast a number older than a change another caller had already made.
				await using (var tick = await match.BeginMutationAsync(token))
				{
					tick.PublishTimer();
				}

				remaining = checkpoint;
			}

			if (!await DelayAsync(remaining, token)) return;

			await using var mutation = await match.BeginMutationAsync(token);

			if (token.IsCancellationRequested) return;
			match.PendingTimer = null;
			match.PendingTimerIsAutoStart = false;
			match.TimerStartedAt = null;
			match.TimerTotalSeconds = null;

			if (autoStart)
			{
				var started = match.InProgress ||
				              await matchLifecycle.StartAsync(match, mutation, token) ==
				              MatchLifecycle.StartOutcome.Started;
				if (started) Announce(match, "Good luck, have fun!");
			}
			else
			{
				Announce(match, "Countdown finished");
			}

			mutation.PublishTimer();
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			logger.LogError(ex, "Countdown loop failed: MatchId={MatchId}", match.DbId);
		}
	}

	/// <summary>Waits the given number of seconds and reports whether the wait was canceled.</summary>
	/// <param name="seconds">The delay length in seconds; zero or negative delays complete immediately.</param>
	/// <param name="token">A token that cancels the wait.</param>
	/// <returns>
	///     <see langword="true" /> when the full delay elapsed; otherwise, <see langword="false" /> when canceled,
	///     meaning the caller should stop.
	/// </returns>
	private static async Task<bool> DelayAsync(int seconds, CancellationToken token)
	{
		if (seconds <= 0) return !token.IsCancellationRequested;

		try
		{
			await Task.Delay(TimeSpan.FromSeconds(seconds), token);
			return true;
		}
		catch (OperationCanceledException)
		{
			return false;
		}
	}

	/// <summary>Posts a message into the match channel from the bot account when the bot is online.</summary>
	/// <param name="match">The match whose channel to announce into.</param>
	/// <param name="text">The message to post.</param>
	private void Announce(MatchSession match, string text)
	{
		var bot = gameRegistry.GetByUserId(BotBootstrapService.BotId);
		if (bot is null) return;

		matchBroadcast.EnqueueChat(match, bot.Name, bot.Id, text);
	}
}
