using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Backgrounds;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Basil.Application.Tests.Backgrounds;

/// <summary>
///     Covers <see cref="MatchRoundEndOutbox" /> (ADR-003): queuing, backpressure, teardown drain, and
///     the retry-then-surface-gap failure path.
/// </summary>
public class MatchRoundEndOutboxTests : IAsyncDisposable
{
	private readonly IMatchRepository _matchRepository = Substitute.For<IMatchRepository>();
	private readonly MatchRoundEndOutbox _outbox;

	public MatchRoundEndOutboxTests()
	{
		_outbox = new MatchRoundEndOutbox(_matchRepository, NullLogger<MatchRoundEndOutbox>.Instance);
	}

	public async ValueTask DisposeAsync()
	{
		await _outbox.StopAsync(CancellationToken.None);
		_outbox.Dispose();
	}

	[Fact]
	public async Task DrainAsync_NoPendingWrites_ReturnsImmediately()
	{
		await _outbox.DrainAsync(matchId: 42).WaitAsync(TimeSpan.FromSeconds(1));
	}

	[Fact]
	public async Task Enqueue_ThenConsumerRuns_PersistsTheWrite()
	{
		await _outbox.StartAsync(CancellationToken.None);
		var write = new RoundEndWrite(MatchId: 1, RoundId: 7, DateTime.UtcNow, Aborted: false);

		_outbox.Enqueue(write);
		await _outbox.DrainAsync(write.MatchId).WaitAsync(TimeSpan.FromSeconds(2));

		await _matchRepository.Received(1)
			.SetRoundEndedAsync(write.RoundId, write.EndedAt, write.Aborted, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Enqueue_ConsumerNeverStarted_QueueFillsThenRejects()
	{
		// No StartAsync: nothing drains the channel, so its bounded capacity (128) is reachable
		// deterministically without racing a live consumer.
		for (var i = 0; i < 128; i++)
			_outbox.Enqueue(new RoundEndWrite(MatchId: 1, RoundId: i, DateTime.UtcNow, Aborted: false));

		Assert.Throws<MatchRoundEndOutboxFullException>(() =>
			_outbox.Enqueue(new RoundEndWrite(MatchId: 1, RoundId: 999, DateTime.UtcNow, Aborted: false)));
	}

	[Fact]
	public async Task Enqueue_WriteUltimatelyFails_RetriesThenGivesUpAndDrainCompletes()
	{
		_matchRepository.SetRoundEndedAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<bool>(),
			Arg.Any<CancellationToken>()).ThrowsForAnyArgs(new InvalidOperationException("db down"));
		await _outbox.StartAsync(CancellationToken.None);
		var write = new RoundEndWrite(MatchId: 3, RoundId: 9, DateTime.UtcNow, Aborted: true);

		_outbox.Enqueue(write);
		// Retry budget is 3 attempts with a short backoff between them; give it generous headroom.
		await _outbox.DrainAsync(write.MatchId).WaitAsync(TimeSpan.FromSeconds(5));

		await _matchRepository.Received(3)
			.SetRoundEndedAsync(write.RoundId, write.EndedAt, write.Aborted, Arg.Any<CancellationToken>());
	}
}