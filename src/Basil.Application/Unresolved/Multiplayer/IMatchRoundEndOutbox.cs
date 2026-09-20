namespace Basil.Application.Unresolved.Multiplayer;

/// <summary>A round-end fact queued for persistence outside a match's lock.</summary>
/// <param name="MatchId">The id of the match the round belongs to, for logging and drain tracking.</param>
/// <param name="RoundId">The id of the round being marked ended.</param>
/// <param name="EndedAt">The time the round ended, in UTC.</param>
/// <param name="Aborted"><see langword="true" /> when the round ended via <c>!mp abort</c>.</param>
public sealed record RoundEndWrite(int MatchId, int RoundId, DateTime EndedAt, bool Aborted);

/// <summary>Thrown by <see cref="IMatchRoundEndOutbox.Enqueue" /> when the outbox has no room left.</summary>
public sealed class MatchRoundEndOutboxFullException(int matchId, int roundId)
	: Exception($"Round-end outbox is full: MatchId={matchId} RoundId={roundId}");

/// <summary>Queues match-round-end persistence outside a match's lock (ADR-003).</summary>
public interface IMatchRoundEndOutbox
{
	/// <summary>
	///     Queues a round-end write for background persistence.
	/// </summary>
	/// <param name="write">The round-end fact to persist.</param>
	/// <exception cref="MatchRoundEndOutboxFullException">The outbox has no room left.</exception>
	void Enqueue(RoundEndWrite write);

	/// <summary>Waits until every write currently queued for a match has been persisted (or given up on).</summary>
	/// <param name="matchId">The id of the match to wait on.</param>
	/// <param name="cancellationToken">A token that cancels the wait.</param>
	Task DrainAsync(int matchId, CancellationToken cancellationToken = default);
}