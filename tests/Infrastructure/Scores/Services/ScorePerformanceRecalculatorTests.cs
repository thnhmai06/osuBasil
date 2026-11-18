using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Ports;
using Basil.Application.Scores.Objects;
using Basil.Application.Scores.Persistence;
using Basil.Application.Shared.Settings;
using Basil.Application.Users.Persistence;
using Basil.Domain.Game.Mechanics;
using Basil.Domain.Game.Scores;
using Basil.Infrastructure.Scores.Logic;
using Basil.Infrastructure.Scores.Services;
using Basil.Testing.Persistence;
using Basil.Shared.Structures.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Basil.Infrastructure.Scores.Tests.Services;

[Collection("Persistence")]
public sealed class ScorePerformanceRecalculatorTests(PersistenceFixture container) : DatabaseTests(container)
{
	private static readonly DateTimeOffset When = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

	private readonly IBeatmapRepository _beatmaps = Substitute.For<IBeatmapRepository>();
	private readonly IBeatmapCalculator _calculator = Substitute.For<IBeatmapCalculator>();
	private readonly BeatmapRecord _map = StoredBeatmaps.Beatmap();
	private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();

	private IScoreRepository Scores => Get<IScoreRepository>();

	private ScorePerformanceRecalculator Create()
	{
		_settings.GetAsync(Arg.Any<CancellationToken>()).Returns(new ServerSettings());
		_beatmaps.GetAsync(_map.Hash, Arg.Any<CancellationToken>()).Returns(_map);
		_calculator.GetPerformanceAsync(Arg.Any<BeatmapRecord>(), Arg.Any<GameMode>(), Arg.Any<GameMods>(),
				Arg.Any<HitCounts>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(new BeatmapPerformance(200, 250));
		var calculator = new ScorePerformanceCalculator(_beatmaps, _calculator, Scores, _settings,
			new FakeTimeProvider(), NullLogger<ScorePerformanceCalculator>.Instance);
		return new ScorePerformanceRecalculator(Scores, calculator, NullLogger<ScorePerformanceRecalculator>.Instance);
	}

	private async Task<ScoreRecord> StoreAsync(string name, Md5Hash? beatmap = null, double? pp = null,
		bool passed = true)
	{
		var player = await Get<IUserRepository>().CreateAsync(new UserData { Name = name });
		return await Scores.CreateAsync(StoredScores.Data(player, beatmap ?? _map.Hash, 1000, When) with
			{ PerformancePoints = pp, IsPassed = passed });
	}

	private async Task<double?> PpOfAsync(ScoreRecord score)
	{
		return (await Scores.GetAsync(score.Id, TestContext.Current.CancellationToken))!.Value.PerformancePoints;
	}

	[Fact]
	public async Task Missing_CalculatesOnlyScoresWithoutPp()
	{
		var missing = await StoreAsync("rc_missing");
		var done = await StoreAsync("rc_done", pp: 5);

		var (count, _) = await Create().CalculateAllAsync(ScorePerformanceScope.Missing,
			TestContext.Current.CancellationToken);

		Assert.Equal(1, count);
		Assert.Equal(200, await PpOfAsync(missing));
		Assert.Equal(5, await PpOfAsync(done));
	}

	[Fact]
	public async Task All_ReplacesThePpOfEveryPassedScore()
	{
		var missing = await StoreAsync("rc_all_missing");
		var done = await StoreAsync("rc_all_done", pp: 5);

		var (count, _) = await Create().CalculateAllAsync(ScorePerformanceScope.All,
			TestContext.Current.CancellationToken);

		Assert.Equal(2, count);
		Assert.Equal(200, await PpOfAsync(missing));
		Assert.Equal(200, await PpOfAsync(done));
	}

	[Fact]
	public async Task FailedScore_StaysWithoutPp()
	{
		var failed = await StoreAsync("rc_failed", passed: false);

		await Create().CalculateAllAsync(ScorePerformanceScope.All, TestContext.Current.CancellationToken);

		Assert.Null(await PpOfAsync(failed));
	}

	[Fact]
	public async Task BeatmapMissing_ScoreSkippedAndRunContinues()
	{
		var unknown = await StoreAsync("rc_unknown", StoredScores.Hash("a beatmap the server lacks"));
		var known = await StoreAsync("rc_known");

		var (done, skipped) = await Create().CalculateAllAsync(ScorePerformanceScope.Missing,
			TestContext.Current.CancellationToken);

		Assert.Equal((1, 1), (done, skipped));
		Assert.Null(await PpOfAsync(unknown));
		Assert.Equal(200, await PpOfAsync(known));
	}

	[Fact]
	public async Task DifficultyFileMissing_ScoreKeepsItsPpAndRunContinues()
	{
		var broken = await StoreAsync("rc_nofile", pp: 9);
		var fine = await StoreAsync("rc_fine");
		var recalculator = Create();
		var calls = 0;
		_calculator.GetPerformanceAsync(Arg.Any<BeatmapRecord>(), Arg.Any<GameMode>(), Arg.Any<GameMods>(),
				Arg.Any<HitCounts>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(_ => ++calls == 1
				? throw new InvalidOperationException("no difficulty file")
				: Task.FromResult(new BeatmapPerformance(200, 250)));

		var (done, skipped) = await recalculator.CalculateAllAsync(ScorePerformanceScope.All,
			TestContext.Current.CancellationToken);

		Assert.Equal((1, 1), (done, skipped));
		Assert.Equal(9, await PpOfAsync(broken));
		Assert.Equal(200, await PpOfAsync(fine));
	}

	[Fact]
	public async Task RunTwice_SameResult()
	{
		var score = await StoreAsync("rc_twice");
		var recalculator = Create();

		await recalculator.CalculateAllAsync(ScorePerformanceScope.All, TestContext.Current.CancellationToken);
		var first = await PpOfAsync(score);
		await recalculator.CalculateAllAsync(ScorePerformanceScope.All, TestContext.Current.CancellationToken);

		Assert.Equal(first, await PpOfAsync(score));
	}

	[Fact]
	public async Task MoreScoresThanOneBatch_AllCalculated()
	{
		var stored = new List<ScoreRecord>();
		for (var i = 0; i < 105; i++) stored.Add(await StoreAsync($"rc_many_{i}"));

		var (done, _) = await Create().CalculateAllAsync(ScorePerformanceScope.Missing,
			TestContext.Current.CancellationToken);

		Assert.Equal(105, done);
		Assert.Equal(200, await PpOfAsync(stored[^1]));
	}

	[Fact]
	public async Task Run_EndsRunningState()
	{
		var recalculator = Create();

		await recalculator.RunAsync(ScorePerformanceScope.Missing, TestContext.Current.CancellationToken);

		Assert.False(recalculator.Running);
	}

	[Fact]
	public async Task StartWhileRunning_Refused()
	{
		var release = new TaskCompletionSource<BeatmapPerformance>();
		await StoreAsync("rc_busy");
		var recalculator = Create();
		_calculator.GetPerformanceAsync(Arg.Any<BeatmapRecord>(), Arg.Any<GameMode>(), Arg.Any<GameMods>(),
				Arg.Any<HitCounts>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(release.Task);

		var first = recalculator.TryStart(ScorePerformanceScope.Missing);
		var second = recalculator.TryStart(ScorePerformanceScope.Missing);
		release.SetResult(new BeatmapPerformance(1, 2));

		Assert.True(first);
		Assert.False(second);
	}
}
