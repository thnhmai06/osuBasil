using System.IO.Compression;
using Basil.Application.Configurations;
using Basil.Infrastructure.Beatmaps;
using Basil.Infrastructure.Persistence;
using Basil.Infrastructure.Persistence.Repositories;
using Basil.Infrastructure.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Tests.Beatmaps;

/// <summary>
///     Thin glue over BeatmapIngestionService's already-tested reconciliation methods — one
///     integration-style test dropping a real beatmapset folder in and polling for the DB row is enough.
///     Deliberately does NOT use the shared <c>SqliteFixture</c>/<c>IClassFixture</c> pattern used
///     elsewhere: xUnit already constructs a fresh instance of this class per test method (no
///     <c>IClassFixture</c> forces sharing), so giving each instance its own temp DB here means the
///     two tests below can never observe each other's rows — no test-order dependency, unlike a
///     shared-DB class fixture would produce for two tests racing the same debounce window.
/// </summary>
[Collection(BeatmapFilesystemTestCollection.Name)]
public class BeatmapWatcherServiceTests : IDisposable
{
	private readonly SqliteBeatmapRepository _beatmaps;
	private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"basil-watcher-test-{Guid.NewGuid():N}.db");
	private readonly CapturingLogger<BeatmapIngestionService> _ingestionLog = new();
	private readonly string _mapsetsPath;
	private readonly BeatmapWatcherService _watcher;
	private readonly CapturingLogger<BeatmapWatcherService> _watcherLog = new();

	public BeatmapWatcherServiceTests()
	{
		// Pooling=False so Dispose can delete the file immediately without SqliteConnection
		// .ClearAllPools() — that call is process-global and would yank pooled connections out
		// from under every other IClassFixture<SqliteFixture>-based test class running in parallel.
		var connectionString = $"Data Source={_dbPath};Foreign Keys=True;Default Timeout=5;Pooling=False";
		SqlMigrationRunner.RunMigrations(connectionString);

		_beatmaps = new SqliteBeatmapRepository(connectionString, NullLogger<SqliteBeatmapRepository>.Instance);
		var mapsets = new SqliteBeatmapsetRepository(connectionString, NullLogger<SqliteBeatmapsetRepository>.Instance);
		_mapsetsPath = Path.Combine(Path.GetTempPath(), "obt-watcher-tests-" + Guid.NewGuid());
		Directory.CreateDirectory(_mapsetsPath);

		var options = Options.Create(new StorageOptions
		{
			ReplaysPath = "",
			AvatarsPath = "",
			MapsetsPath = _mapsetsPath,
			MenuSeasonalsPath = "",
			MenuBannersPath = "",
			FaqsPath = "", CachePath = Path.Combine(_mapsetsPath, "Cache")
		});
		var ingestion = new BeatmapIngestionService(_beatmaps, mapsets, new FakeOsuCalculator(), options,
			new FileSystemResponseCache(options), _ingestionLog);
		_watcher = new BeatmapWatcherService(ingestion, options, _watcherLog);
	}

	public void Dispose()
	{
		Directory.Delete(_mapsetsPath, true);
		// Pooling=False above means there's nothing pooled to clear before deleting.
		File.Delete(_dbPath);
		File.Delete(_dbPath + "-wal");
		File.Delete(_dbPath + "-shm");
	}

	[Fact]
	public async Task DroppingMapsetFolder_GetsAutoIngestedWithinDebounceWindow()
	{
		await _watcher.StartAsync(CancellationToken.None);
		try
		{
			// FileSystemWatcher can silently miss the very first filesystem event after a process's
			// first watcher is armed (a known .NET/Windows cold-start quirk) — a throwaway warm-up
			// event before the real payload avoids that race.
			await File.WriteAllTextAsync(Path.Combine(_mapsetsPath, "warmup.txt"), "");
			await Task.Delay(300);
			File.Delete(Path.Combine(_mapsetsPath, "warmup.txt"));

			var folder = Path.Combine(_mapsetsPath, "900000000 FAIRY FORE - Vivid");
			Directory.CreateDirectory(folder);
			File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "vivid_osu_file.osu"),
				Path.Combine(folder, "vivid_osu_file.osu"));

			var deadline = DateTime.UtcNow.AddSeconds(10);
			while (DateTime.UtcNow < deadline &&
			       await _beatmaps.FetchOneAsync(filename: "vivid_osu_file.osu", includePrivate: true) is null)
				await Task.Delay(200);

			var found = await _beatmaps.FetchOneAsync(filename: "vivid_osu_file.osu", includePrivate: true);
			Assert.True(found is not null,
				"Beatmap never appeared. Ingestion log: " + string.Join(" | ", _ingestionLog.Messages) +
				" || Watcher log: " + string.Join(" | ", _watcherLog.Messages));
		}
		finally
		{
			await _watcher.StopAsync(CancellationToken.None);
		}
	}

	[Fact]
	public async Task RenamingMapsetFolderToDeletedMarker_RemovesMapsetWithoutReingestingIt()
	{
		await _watcher.StartAsync(CancellationToken.None);
		try
		{
			await File.WriteAllTextAsync(Path.Combine(_mapsetsPath, "warmup.txt"), "");
			await Task.Delay(300);
			File.Delete(Path.Combine(_mapsetsPath, "warmup.txt"));

			var folder = Path.Combine(_mapsetsPath, "unresolved FAIRY FORE - Vivid");
			Directory.CreateDirectory(folder);
			File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "vivid_osu_file.osu"),
				Path.Combine(folder, "vivid_osu_file.osu"));

			var ingestDeadline = DateTime.UtcNow.AddSeconds(10);
			while (DateTime.UtcNow < ingestDeadline &&
			       await _beatmaps.FetchOneAsync(filename: "vivid_osu_file.osu", includePrivate: true) is null)
				await Task.Delay(200);

			var beatmap = await _beatmaps.FetchOneAsync(filename: "vivid_osu_file.osu", includePrivate: true);
			Assert.NotNull(beatmap);

			// ReconcileDeletedFolderAsync (which this test exercises indirectly through the watcher)
			// parses the Beatmapset id from the folder's own leading digits — rename to the actually
			// resolved id first, matching every other test here that relies on that lookup.
			var resolvedFolder = BeatmapIngestionService.MapsetFolderPath(
				new StorageOptions
				{
					ReplaysPath = "",
					AvatarsPath = "",
					MapsetsPath = _mapsetsPath,
					MenuSeasonalsPath = "",
					MenuBannersPath = "",
					FaqsPath = "", CachePath = ""
				},
				beatmap.Beatmapset);
			Directory.Move(folder, resolvedFolder);

			var deletedFolder = resolvedFolder + BeatmapIngestionService.DeletedFolderInfix +
			                    Guid.NewGuid().ToString("N");
			Directory.Move(resolvedFolder, deletedFolder);

			var deleteDeadline = DateTime.UtcNow.AddSeconds(10);
			while (DateTime.UtcNow < deleteDeadline &&
			       await _beatmaps.FetchOneAsync(setId: beatmap.Beatmapset.Id, includePrivate: true) is not null)
				await Task.Delay(200);

			Assert.Null(await _beatmaps.FetchOneAsync(setId: beatmap.Beatmapset.Id, includePrivate: true));
		}
		finally
		{
			await _watcher.StopAsync(CancellationToken.None);
		}
	}

	[Fact]
	public async Task DroppingOsz_SelfDeletingAfterExtraction_DoesNotDeleteTheMapsetItJustIngested()
	{
		await _watcher.StartAsync(CancellationToken.None);
		try
		{
			// FileSystemWatcher can silently miss the very first filesystem event after a process's
			// first watcher is armed (a known .NET/Windows cold-start quirk) — a throwaway warm-up
			// event before the real payload avoids that race.
			await File.WriteAllTextAsync(Path.Combine(_mapsetsPath, "warmup.txt"), "");
			await Task.Delay(300);
			File.Delete(Path.Combine(_mapsetsPath, "warmup.txt"));

			// Fixture carries an explicit BeatmapSetID (900000) so the beatmapset ResolveMapsetAsync
			// creates has a deterministic id, and the .osz's own filename below uses that same id —
			// matching what a real osu!-downloaded archive looks like ("{setId} Artist - Title.osz").
			// Without that alignment ReconcileDeletedFolderAsync (parsing the leading digits of
			// whatever path it's given) would parse an id that happens not to match any row, and the
			// bug this test exists to catch — deleting the just-ingested beatmapset — would go unnoticed.
			var oszPath = Path.Combine(_mapsetsPath, "900000 FAIRY FORE - Vivid.osz");
			await using (var archive = await ZipFile.OpenAsync(oszPath, ZipArchiveMode.Create))
			{
				await archive.CreateEntryFromFileAsync(
					Path.Combine(AppContext.BaseDirectory, "Fixtures", "vivid_with_setid.osu"),
					"vivid_with_setid.osu");
			}

			var ingestDeadline = DateTime.UtcNow.AddSeconds(10);
			while (DateTime.UtcNow < ingestDeadline &&
			       await _beatmaps.FetchOneAsync(setId: 900000, includePrivate: true) is null)
				await Task.Delay(200);

			Assert.True(await _beatmaps.FetchOneAsync(setId: 900000, includePrivate: true) is not null,
				"Beatmap never appeared. Ingestion log: " + string.Join(" | ", _ingestionLog.Messages) +
				" || Watcher log: " + string.Join(" | ", _watcherLog.Messages));

			// ReconcileOszAsync deletes the .osz right after extracting it, which raises this same
			// watcher's own Deleted event for that path — poll out a generous window (rather than a
			// single fixed sleep, which under CI-level parallel load can be too short) watching for
			// the beatmapset to vanish; it must not, for the whole window.
			var survivalDeadline = DateTime.UtcNow.AddSeconds(10);
			while (DateTime.UtcNow < survivalDeadline &&
			       await _beatmaps.FetchOneAsync(setId: 900000, includePrivate: true) is not null)
				await Task.Delay(200);

			var survived = await _beatmaps.FetchOneAsync(setId: 900000, includePrivate: true);
			Assert.True(survived is not null,
				"Beatmapset was deleted after its own .osz's post-extraction self-delete. Ingestion log: " +
				string.Join(" | ", _ingestionLog.Messages) +
				" || Watcher log: " + string.Join(" | ", _watcherLog.Messages));
		}
		finally
		{
			await _watcher.StopAsync(CancellationToken.None);
		}
	}

	private sealed class CapturingLogger<T> : ILogger<T>
	{
		public readonly List<string> Messages = [];

		public IDisposable? BeginScope<TState>(TState state) where TState : notnull
		{
			return null;
		}

		public bool IsEnabled(LogLevel logLevel)
		{
			return true;
		}

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
			Func<TState, Exception?, string> formatter)
		{
			Messages.Add($"[{logLevel}] {formatter(state, exception)} {exception}");
		}
	}
}