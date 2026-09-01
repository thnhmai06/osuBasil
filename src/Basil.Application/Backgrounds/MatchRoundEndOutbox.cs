using System.Collections.Concurrent;
using System.Threading.Channels;
using Basil.Application.Abstractions.Multiplayer;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Backgrounds;

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

/// <summary>
///     Persists round-end writes outside <c>MatchSession.Lock</c> via a single shared, ordered queue
///     (ADR-003 Option A1).
/// </summary>
/// <remarks>
///     One shared background consumer drains every match's writes from a single bounded channel, in
///     the order they were enqueued. Per-match FIFO order falls out for free: a match's own round-end
///     writes are always enqueued one at a time, under that match's own lock, so two writes for the
///     same match can never race each other into the channel out of order. Round-*start*
///     (<see cref="IMatchRepository.CreateRoundAsync" />) is out of scope — it stays synchronous,
///     since <c>MatchSession.CurrentRoundId</c> is read immediately after by score submission and
///     cannot be deferred (ADR-003, decision A1).
/// </remarks>
/// <param name="matchRepository">The repository the background consumer persists round-end writes through.</param>
/// <param name="logger">The logger used for retry warnings and a permanently-failed write's gap record.</param>
public sealed class MatchRoundEndOutbox(IMatchRepository matchRepository, ILogger<MatchRoundEndOutbox> logger)
	: BackgroundService, IMatchRoundEndOutbox
{
	/// <summary>Bounded queue capacity — round-end writes are rare; a full queue is an anomaly, not normal load.</summary>
	private const int Capacity = 128;

	private const int MaxAttempts = 3;

	private readonly Channel<RoundEndWrite> _channel = Channel.CreateBounded<RoundEndWrite>(Capacity);
	private readonly ConcurrentDictionary<int, int> _pendingByMatch = new();

	/// <inheritdoc />
	/// <remarks>
	///     Rejects immediately (rather than blocking the lock-held caller, or dropping an older queued
	///     write) when the shared queue is full, per ADR-003's backpressure decision.
	/// </remarks>
	public void Enqueue(RoundEndWrite write)
	{
		_pendingByMatch.AddOrUpdate(write.MatchId, 1, static (_, count) => count + 1);

		if (_channel.Writer.TryWrite(write)) return;

		Decrement(write.MatchId);
		throw new MatchRoundEndOutboxFullException(write.MatchId, write.RoundId);
	}

	/// <inheritdoc />
	public async Task DrainAsync(int matchId, CancellationToken cancellationToken = default)
	{
		while (_pendingByMatch.TryGetValue(matchId, out var pending) && pending > 0)
			await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
	}

	/// <inheritdoc />
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		await foreach (var write in _channel.Reader.ReadAllAsync(stoppingToken))
			await ProcessAsync(write, stoppingToken);
	}

	/// <summary>
	///     Persists one queued write, retrying a bounded number of times on failure before logging it
	///     as a known, unrecovered gap (ADR-003's retry-then-surface-gap failure semantics).
	/// </summary>
	private async Task ProcessAsync(RoundEndWrite write, CancellationToken cancellationToken)
	{
		try
		{
			for (var attempt = 1;; attempt++)
				try
				{
					await matchRepository.SetRoundEndedAsync(write.RoundId, write.EndedAt, write.Aborted,
						cancellationToken);
					return;
				}
				catch (Exception ex) when (ex is not OperationCanceledException && attempt < MaxAttempts)
				{
					logger.LogWarning(ex,
						"Round-end write failed, retrying: Attempt={Attempt} MatchId={MatchId} RoundId={RoundId}",
						attempt, write.MatchId, write.RoundId);
					await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken);
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					// Retry budget exhausted: the round's EndedAt never made it to the database. This is
					// the known-gap outcome ADR-003 requires when persistence ultimately fails — surfaced
					// here with every fact needed to reconstruct it by hand (round, match, intended
					// end time, whether it was an abort), never silently dropped.
					logger.LogError(ex,
						"Round-end write permanently failed after {MaxAttempts} attempts, known gap: " +
						"MatchId={MatchId} RoundId={RoundId} EndedAt={EndedAt} Aborted={Aborted}",
						MaxAttempts, write.MatchId, write.RoundId, write.EndedAt, write.Aborted);
					return;
				}
		}
		finally
		{
			Decrement(write.MatchId);
		}
	}

	/// <summary>Decrements a match's pending count, removing the entry once it reaches zero.</summary>
	private void Decrement(int matchId)
	{
		while (_pendingByMatch.TryGetValue(matchId, out var current))
		{
			var updated = current - 1;
			if (updated <= 0)
			{
				if (_pendingByMatch.TryRemove(new KeyValuePair<int, int>(matchId, current))) return;
			}
			else if (_pendingByMatch.TryUpdate(matchId, updated, current))
			{
				return;
			}
		}
	}
}
