using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Ports;
using Basil.Application.Scores.Persistence;
using Basil.Application.Shared.Settings;
using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;
using Basil.Infrastructure.Scores.Logic;
using Basil.Testing.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Basil.Infrastructure.Scores.Tests.Logic;

public sealed class ScorePerformanceCalculatorTests
{
	private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

	private readonly IBeatmapRepository _beatmaps = Substitute.For<IBeatmapRepository>();
	private readonly IBeatmapCalculator _calculator = Substitute.For<IBeatmapCalculator>();
	private readonly FakeTimeProvider _clock = new();
	private readonly BeatmapRecord _map = StoredBeatmaps.Beatmap();
	private readonly IScoreRepository _scores = Substitute.For<IScoreRepository>();
	private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();

	public ScorePerformanceCalculatorTests()
	{
		_settings.GetAsync(Arg.Any<CancellationToken>())
			.Returns(new ServerSettings { Beatmaps = new ServerSettings.BeatmapSettings { PerformanceTimeout = Limit } });
		_beatmaps.GetAsync(_map.Hash, Arg.Any<CancellationToken>()).Returns(_map);
	}

	private ScorePerformanceCalculator Create()
	{
		return new ScorePerformanceCalculator(_beatmaps, _calculator, _scores, _settings, _clock,
			NullLogger<ScorePerformanceCalculator>.Instance);
	}

	private ScoreRecord Score(bool passed = true)
	{
		return new ScoreRecord
			{ Id = 7, Value = StoredScores.Data(null, _map.Hash, 1000, DateTimeOffset.UnixEpoch) with { IsPassed = passed } };
	}

	private void CalculatorGives(double pp)
	{
		_calculator.GetPerformanceAsync(Arg.Any<BeatmapRecord>(), Arg.Any<GameMode>(), Arg.Any<GameMods>(),
				Arg.Any<HitCounts>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(new BeatmapPerformance(pp, pp + 10));
	}

	[Fact]
	public async Task PassedScoreOfKnownBeatmap_PpStored()
	{
		CalculatorGives(123.4);

		var stored = await Create().CalculateAsync(Score(), TestContext.Current.CancellationToken);

		Assert.True(stored);
		await _scores.Received(1).SetPpAsync(7, 123.4);
	}

	[Fact]
	public async Task CalculatorThrows_PpStaysNull()
	{
		_calculator.GetPerformanceAsync(Arg.Any<BeatmapRecord>(), Arg.Any<GameMode>(), Arg.Any<GameMods>(),
				Arg.Any<HitCounts>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns<Task<BeatmapPerformance>>(_ => throw new InvalidOperationException("no difficulty file"));

		var stored = await Create().CalculateAsync(Score(), TestContext.Current.CancellationToken);

		Assert.False(stored);
		await _scores.DidNotReceiveWithAnyArgs().SetPpAsync(default, default);
	}

	[Fact]
	public async Task CalculationOutlastsTimeout_PpStaysNull()
	{
		_calculator.GetPerformanceAsync(Arg.Any<BeatmapRecord>(), Arg.Any<GameMode>(), Arg.Any<GameMods>(),
				Arg.Any<HitCounts>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(call => NeverFinishes(call.Arg<CancellationToken>()));

		var pending = Create().CalculateAsync(Score(), TestContext.Current.CancellationToken);
		_clock.Advance(Limit);
		var stored = await pending;

		Assert.False(stored);
		await _scores.DidNotReceiveWithAnyArgs().SetPpAsync(default, default);
	}

	[Fact]
	public async Task CalculationWithinTimeout_PpStored()
	{
		CalculatorGives(50);

		var pending = Create().CalculateAsync(Score(), TestContext.Current.CancellationToken);
		_clock.Advance(Limit - TimeSpan.FromSeconds(1));

		Assert.True(await pending);
	}

	[Fact]
	public async Task ShutdownDuringCalculation_Cancels()
	{
		using var shutdown = new CancellationTokenSource();
		_calculator.GetPerformanceAsync(Arg.Any<BeatmapRecord>(), Arg.Any<GameMode>(), Arg.Any<GameMods>(),
				Arg.Any<HitCounts>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(call => NeverFinishes(call.Arg<CancellationToken>()));

		var pending = Create().CalculateAsync(Score(), shutdown.Token);
		await shutdown.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
	}

	[Fact]
	public async Task FailedScore_NeverGetsPp()
	{
		CalculatorGives(100);

		var stored = await Create().CalculateAsync(Score(false), TestContext.Current.CancellationToken);

		Assert.False(stored);
		await _scores.DidNotReceiveWithAnyArgs().SetPpAsync(default, default);
	}

	[Fact]
	public async Task BeatmapTheServerLacks_NeverGetsPp()
	{
		CalculatorGives(100);
		_beatmaps.GetAsync(_map.Hash, Arg.Any<CancellationToken>()).Returns((BeatmapRecord?)null);

		var stored = await Create().CalculateAsync(Score(), TestContext.Current.CancellationToken);

		Assert.False(stored);
		await _scores.DidNotReceiveWithAnyArgs().SetPpAsync(default, default);
	}

	private static async Task<BeatmapPerformance> NeverFinishes(CancellationToken ct)
	{
		await Task.Delay(Timeout.Infinite, ct);
		return default;
	}
}
