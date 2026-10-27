using Basil.Application.Beatmaps.Persistence;
using Basil.Infrastructure.Beatmaps.Runtime;
using Basil.Infrastructure.Beatmaps.Tests.Support;
using Basil.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Basil.Infrastructure.Beatmaps.Tests.Runtime.Beatmaps;

/// <summary>Pins how the watcher starts, keeps watching after a failure, and releases its watch.</summary>
public sealed class BeatmapsetStorageWatcherTests
{
	/// <summary>How long a test waits for the watcher to take its next watch before calling it dead.</summary>
	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

	/// <summary>
	///     A start-up scan that was cancelled leaves the watch never enumerated; stopping the service must still release
	///     it.
	/// </summary>
	[Fact]
	public async Task StopAsync_ReleasesTheWatchWhenTheStartUpScanWasCancelled()
	{
		using var app = new TestApplication();
		var storage = app.Provider.GetRequiredService<IBeatmapsetStorage>();
		var watch = new FakeWatch();
		storage.WatchAsync(Arg.Any<CancellationToken>()).Returns(_ => watch.Next());
		storage.ListAsync(Arg.Any<CancellationToken>()).Returns<Task<IEnumerable<BeatmapsetSource>>>(_ =>
			Task.FromException<IEnumerable<BeatmapsetSource>>(new OperationCanceledException()));
		using var watcher = new BeatmapsetStorageWatcher(app.Beatmapsets, storage, app.Clock,
			NullLogger<BeatmapsetStorageWatcher>.Instance);

		using var startup = new CancellationTokenSource();
		await startup.CancelAsync();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => watcher.StartAsync(startup.Token));

		await watcher.StopAsync(TestContext.Current.CancellationToken);

		Assert.Equal(1, watch.Count);
		Assert.True(watch[0].Released);
	}

	/// <summary>
	///     A watch that fails must not end the watching: once the restart delay has passed, the storage is watched
	///     again.
	/// </summary>
	[Fact]
	public async Task ExecuteAsync_WatchesAgainAfterTheWatchFails()
	{
		using var app = new TestApplication();
		var storage = app.Provider.GetRequiredService<IBeatmapsetStorage>();
		var watch = new FakeWatch();
		var watches = 0;
		storage.WatchAsync(Arg.Any<CancellationToken>())
			.Returns(_ => watch.Next(Interlocked.Increment(ref watches) == 1 ? new FileNotFoundException() : null));
		storage.ListAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IEnumerable<BeatmapsetSource>>([]));
		var time = new SignalingTimeProvider(app.Clock);
		using var watcher = new BeatmapsetStorageWatcher(app.Beatmapsets, storage, time,
			NullLogger<BeatmapsetStorageWatcher>.Instance);

		await watcher.StartAsync(TestContext.Current.CancellationToken);
		await watch[0].Ending.WaitAsync(Patience, TestContext.Current.CancellationToken);

		await time.TimerCreated.WaitAsync(Patience, TestContext.Current.CancellationToken);
		app.Clock.Advance(TimeSpan.FromMinutes(1));
		var second = await watch.WaitAsync(1, Patience);

		Assert.NotNull(second);
		Assert.True(watch[0].Released);
		await watcher.StopAsync(TestContext.Current.CancellationToken);
	}

	/// <summary>
	///     A watch that stops by a cancellation nobody asked for is a failure like any other, so the storage is watched
	///     again.
	/// </summary>
	[Fact]
	public async Task ExecuteAsync_WatchesAgainAfterAWatchCancelledWithoutStopping()
	{
		using var app = new TestApplication();
		var storage = app.Provider.GetRequiredService<IBeatmapsetStorage>();
		var watch = new FakeWatch();
		var watches = 0;
		storage.WatchAsync(Arg.Any<CancellationToken>())
			.Returns(_ => watch.Next(Interlocked.Increment(ref watches) == 1
				? new OperationCanceledException()
				: null));
		storage.ListAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IEnumerable<BeatmapsetSource>>([]));
		var time = new SignalingTimeProvider(app.Clock);
		using var watcher = new BeatmapsetStorageWatcher(app.Beatmapsets, storage, time,
			NullLogger<BeatmapsetStorageWatcher>.Instance);

		await watcher.StartAsync(TestContext.Current.CancellationToken);
		await watch[0].Ending.WaitAsync(Patience, TestContext.Current.CancellationToken);

		await time.TimerCreated.WaitAsync(Patience, TestContext.Current.CancellationToken);
		app.Clock.Advance(TimeSpan.FromMinutes(1));
		var second = await watch.WaitAsync(1, Patience);

		Assert.NotNull(second);
		await watcher.StopAsync(TestContext.Current.CancellationToken);
	}
}