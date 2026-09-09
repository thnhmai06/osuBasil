using Microsoft.Extensions.Logging;

namespace Basil.Server.Features.Multiplayer.Handlers.Countdown;

/// <summary>Cancels a match's pending countdown.</summary>
public sealed class AbortTimerHandler(ILogger<AbortTimerHandler> logger)
{
	public enum AbortTimerResult : byte
	{
		Ok,
		NoTimerRunning
	}

	/// <summary>Cancels a pending countdown and republishes the timer state.</summary>
	/// <param name="match">The match whose timer to abort.</param>
	/// <param name="mutation">The open mutation scope that publishes the resulting timer.</param>
	/// <returns>
	///     <see cref="AbortTimerResult.Ok" /> when a timer was running and was canceled, or
	///     <see cref="AbortTimerResult.NoTimerRunning" /> when none was.
	/// </returns>
	public AbortTimerResult AbortTimer(MatchSession match, MatchMutationScope mutation)
	{
		if (match.PendingTimer is null) return AbortTimerResult.NoTimerRunning;

		match.PendingTimer.Cancel();
		match.PendingTimer = null;
		match.PendingTimerIsAutoStart = false;
		match.TimerStartedAt = null;
		match.TimerTotalSeconds = null;
		logger.LogDebug("Timer aborted: MatchId={MatchId}", match.DbId);
		mutation.PublishTimer();
		return AbortTimerResult.Ok;
	}
}
