using Basil.Domain.Multiplayer;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging;

namespace Basil.Server.Features.Multiplayer.Handlers.Lifecycle;

/// <summary>Stops an in-progress match.</summary>
public sealed class AbortHandler(
	MatchBroadcast matchBroadcast,
	IMatchRoundEndOutbox roundEndOutbox,
	ILogger<AbortHandler> logger)
{
	public enum AbortResult : byte
	{
		Ok,
		NotInProgress
	}

	/// <summary>Stops an in-progress match, unreadying playing players and ending the current round.</summary>
	/// <param name="match">The match to abort.</param>
	/// <param name="mutation">The open mutation scope that publishes the resulting state.</param>
	/// <param name="cancellationToken">
	///     Ignored: the eventual publish is canceled by the token given to
	///     <see cref="MatchSession.BeginMutationAsync" /> when <paramref name="mutation" /> was opened.
	/// </param>
	/// <returns>
	///     <see cref="AbortResult.Ok" /> when the match was aborted, or
	///     <see cref="AbortResult.NotInProgress" /> when it was not running.
	/// </returns>
	public Task<AbortResult> AbortAsync(MatchSession match, MatchMutationScope mutation,
		CancellationToken cancellationToken = default)
	{
		if (!match.InProgress) return Task.FromResult(AbortResult.NotInProgress);

		match.UnreadyPlayers(SlotStatus.Playing);
		match.ResetPlayersLoadedStatus();
		match.InProgress = false;

		var roundId = match.CurrentRoundId;
		if (roundId is { } id)
		{
			// The round ends here in memory regardless of whether the queued write persists —
			// CurrentRoundId is cleared unconditionally, matching the prior synchronous behavior.
			match.CurrentRoundId = null;
			try
			{
				roundEndOutbox.Enqueue(new RoundEndWrite(match.DbId, id, DateTimeOffset.UtcNow.UtcDateTime, true));
			}
			catch (MatchRoundEndOutboxFullException ex)
			{
				logger.LogError(ex, "Round-end write rejected, outbox full: MatchId={MatchId} RoundId={RoundId}",
					match.DbId, id);
			}
		}

		logger.LogInformation("Match aborted: MatchId={MatchId} RoundId={RoundId}", match.DbId, roundId);
		matchBroadcast.Enqueue(match, ServerPacketWriter.MatchAbort(), false);
		matchBroadcast.AnnounceToRoomAndReferees(match, "Match aborted.");
		mutation.PublishState();
		return Task.FromResult(AbortResult.Ok);
	}
}