using Basil.Application.Persistence.Beatmaps;
using Basil.Infrastructure.Persistence.Beatmaps;
using Basil.Infrastructure.Persistence.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Basil.Infrastructure.Persistence.Tests.Beatmaps;

/// <summary>Contract tests of the watching of the beatmapset storage.</summary>
public sealed class FileBeatmapsetWatcherTests : IDisposable
{
	private readonly TempData _data = new();
	private readonly CancellationTokenSource _stop = new();
	private readonly FakeTimeProvider _time = new();

	private string Sets => _data.Paths.Beatmapsets.Path;

	public void Dispose()
	{
		_stop.Dispose();
		_data.Dispose();
	}

	[Fact]
	public void Cancelling_the_token_stops_a_watch_nobody_reads()
	{
		using var watcher = Start();

		_stop.Cancel();

		Assert.True(watcher.IsStopped);
	}

	[Fact]
	public void Disposing_the_watch_stops_it()
	{
		var watcher = Start();

		watcher.Dispose();

		Assert.True(watcher.IsStopped);
	}

	[Fact]
	public async Task A_burst_of_changes_to_one_name_is_reported_once_after_it_settled()
	{
		Directory.CreateDirectory(Path.Combine(Sets, "1 Burst - Folder"));
		Directory.CreateDirectory(Path.Combine(Sets, "2 Sentinel - Folder"));
		using var watcher = Start();
		await using var reports = watcher.GetAsyncEnumerator(TestContext.Current.CancellationToken);

		// Each change comes before the previous one has settled, so the settling time starts again.
		for (var change = 0; change < 5; change++)
		{
			watcher.Report("1 Burst - Folder");
			_time.Advance(FileBeatmapsetWatcher.SettleTime - TimeSpan.FromSeconds(1));
		}

		_time.Advance(TimeSpan.FromSeconds(1));
		Assert.True(await reports.MoveNextAsync());
		Assert.Equal("1 Burst - Folder", reports.Current.Name);

		watcher.Report("2 Sentinel - Folder");
		_time.Advance(FileBeatmapsetWatcher.SettleTime);
		Assert.True(await reports.MoveNextAsync());
		Assert.Equal("2 Sentinel - Folder", reports.Current.Name);
	}

	[Fact]
	public async Task Reading_ends_when_the_watch_is_stopped()
	{
		using var watcher = Start();
		await using var reports = watcher.GetAsyncEnumerator(TestContext.Current.CancellationToken);

		_stop.Cancel();

		Assert.False(await reports.MoveNextAsync());
	}

	private FileBeatmapsetWatcher Start()
	{
		return new FileBeatmapsetWatcher(Sets, _time, NullLogger.Instance, () => Array.Empty<BeatmapsetSource>(),
			_stop.Token);
	}
}