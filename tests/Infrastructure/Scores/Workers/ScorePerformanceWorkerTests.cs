using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Ports;
using Basil.Application.Scores.Objects;
using Basil.Application.Scores.Persistence;
using Basil.Application.Scores.Services;
using Basil.Application.Shared.Settings;
using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;
using Basil.Infrastructure.Scores.Logic;
using Basil.Infrastructure.Scores.Workers;
using Basil.Testing.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Basil.Infrastructure.Scores.Tests.Workers;

/// <summary>What the pp worker does with one queued score and at startup, and that a failure never stops it.</summary>
public sealed class ScorePerformanceWorkerTests
{
	private readonly IBeatmapCalculator _calculator = Substitute.For<IBeatmapCalculator>();
	private readonly BeatmapRecord _map = StoredBeatmaps.Beatmap();
	private readonly IScorePerformanceRecalculator _recalculator = Substitute.For<IScorePerformanceRecalculator>();
	private readonly IScoreRepository _scores = Substitute.For<IScoreRepository>();

	private ScorePerformanceWorker Create()
	{
		var beatmaps = Substitute.For<IBeatmapRepository>();
		beatmaps.GetAsync(_map.Hash, Arg.Any<CancellationToken>()).Returns(_map);
		var settings = Substitute.For<ISettingsRepository>();
		settings.GetAsync(Arg.Any<CancellationToken>()).Returns(new ServerSettings());
		_calculator.GetPerformanceAsync(Arg.Any<BeatmapRecord>(), Arg.Any<GameMode>(), Arg.Any<GameMods>(),
				Arg.Any<HitCounts>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(new BeatmapPerformance(100, 120));
		var calculator = new ScorePerformanceCalculator(beatmaps, _calculator, _scores, settings,
			new FakeTimeProvider(), NullLogger<ScorePerformanceCalculator>.Instance);
		return new ScorePerformanceWorker(new ScorePerformanceQueue(), _scores, calculator, _recalculator,
			NullLogger<ScorePerformanceWorker>.Instance);
	}

	private ScoreRecord Score(int id)
	{
		return new ScoreRecord
			{ Id = id, Value = StoredScores.Data(null, _map.Hash, 1000, DateTimeOffset.UnixEpoch) };
	}

	[Fact]
	public async Task QueuedScore_GetsItsPp()
	{
		_scores.GetAsync(5, Arg.Any<CancellationToken>()).Returns(Score(5));

		await Create().CalculateAsync(5, TestContext.Current.CancellationToken);

		await _scores.Received(1).SetPpAsync(5, 100);
	}

	[Fact]
	public async Task ScoreGone_NothingHappens()
	{
		_scores.GetAsync(5, Arg.Any<CancellationToken>()).Returns((ScoreRecord?)null);

		await Create().CalculateAsync(5, TestContext.Current.CancellationToken);

		await _scores.DidNotReceiveWithAnyArgs().SetPpAsync(default, default);
	}

	[Fact]
	public async Task ReadFails_DoesNotThrowAndNextScoreStillGetsPp()
	{
		_scores.GetAsync(5, Arg.Any<CancellationToken>()).Returns<ValueTask<ScoreRecord?>>(_ => throw new IOException());
		_scores.GetAsync(6, Arg.Any<CancellationToken>()).Returns(Score(6));
		var worker = Create();

		await worker.CalculateAsync(5, TestContext.Current.CancellationToken);
		await worker.CalculateAsync(6, TestContext.Current.CancellationToken);

		await _scores.Received(1).SetPpAsync(6, 100);
	}

	[Fact]
	public async Task StartupWithScoresLackingPp_StartsMissingScope()
	{
		_scores.ListForPpAsync(0, true, 1, Arg.Any<CancellationToken>()).Returns([Score(1)]);

		await Create().StartMissingAsync(TestContext.Current.CancellationToken);

		_recalculator.Received(1).TryStart(ScorePerformanceScope.Missing);
	}

	[Fact]
	public async Task StartupWithNoScoreLackingPp_StartsNothing()
	{
		_scores.ListForPpAsync(0, true, 1, Arg.Any<CancellationToken>()).Returns([]);

		await Create().StartMissingAsync(TestContext.Current.CancellationToken);

		_recalculator.DidNotReceiveWithAnyArgs().TryStart(default);
	}

	[Fact]
	public async Task StartupLookupFails_DoesNotThrow()
	{
		_scores.ListForPpAsync(0, true, 1, Arg.Any<CancellationToken>())
			.Returns<Task<IReadOnlyList<ScoreRecord>>>(_ => throw new IOException());

		await Create().StartMissingAsync(TestContext.Current.CancellationToken);

		_recalculator.DidNotReceiveWithAnyArgs().TryStart(default);
	}
}
