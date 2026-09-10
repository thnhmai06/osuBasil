using Basil.Server.Features.Multiplayer.Handlers.Countdown;

namespace Basil.Server.Features.Multiplayer.Handlers.Lifecycle;

/// <summary>Starts a match immediately, or queues the countdown that starts it later.</summary>
public sealed class StartHandler(MatchLifecycle matchLifecycle, TimerHandler timerHandler)
{
	public enum StartResult : byte
	{
		AlreadyInProgress,
		Started,
		CountdownQueued,
		BeatmapMissing,
		NoOccupiedSlots
	}

	/// <summary>Starts the match immediately, or queues a countdown when one is requested.</summary>
	/// <remarks>
	///     A <see langword="null" /> or non-positive <paramref name="countdownSeconds" /> starts immediately instead of
	///     queuing.
	/// </remarks>
	/// <param name="match">The match to start.</param>
	/// <param name="countdownSeconds">The countdown length in seconds, or <see langword="null" /> to start immediately.</param>
	/// <param name="mutation">The open mutation scope that publishes the resulting timer or match state.</param>
	/// <param name="cancellationToken">A token that cancels the immediate start.</param>
	/// <returns>
	///     <see cref="StartResult.AlreadyInProgress" /> when the match is already running,
	///     <see cref="StartResult.CountdownQueued" /> when a countdown was queued, or
	///     <see cref="StartResult.Started" />, <see cref="StartResult.BeatmapMissing" />, or
	///     <see cref="StartResult.NoOccupiedSlots" /> for an immediate start.
	/// </returns>
	public async Task<StartResult> StartAsync(MatchSession match, int? countdownSeconds, MatchMutationScope mutation,
		CancellationToken cancellationToken = default)
	{
		if (match.InProgress) return StartResult.AlreadyInProgress;

		if (countdownSeconds is > 0)
		{
			timerHandler.BeginCountdown(match, countdownSeconds.Value, true, mutation);
			return StartResult.CountdownQueued;
		}

		// An immediate start must stop any countdown/timer already pending, or its background loop
		// keeps running (and can still fire "Match starts in N seconds"/try to auto-start again) even
		// though the match started right now through this call instead.
		if (timerHandler.CancelPendingTimer(match, announce: false))
			mutation.PublishTimer();

		var outcome = await matchLifecycle.StartAsync(match, mutation, cancellationToken);
		return outcome switch
		{
			MatchLifecycle.StartOutcome.Started => StartResult.Started,
			MatchLifecycle.StartOutcome.NoOccupiedSlots => StartResult.NoOccupiedSlots,
			_ => StartResult.BeatmapMissing
		};
	}
}