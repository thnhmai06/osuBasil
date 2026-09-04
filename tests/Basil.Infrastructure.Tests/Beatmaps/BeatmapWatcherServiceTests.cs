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
	private readonly SqliteBeatmapsetRepository _beatmapsets;
	private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"basil-watcher-test-{Guid.NewGuid():N}.db");
	private readonly CapturingLogger<BeatmapIngestionService> _ingestionLog = new();
	private readonly string _beatmapsetsPath;
	private readonly IOptions<StorageOptions> _options;
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
		_beatmapsets =
			new SqliteBeatmapsetRepository(connectionString, NullLogger<SqliteBeatmapsetRepository>.Instance);
		_beatmapsetsPath = Path.Combine(Path.GetTempPath(), "obt-watcher-tests-" + Guid.NewGuid());
		Directory.CreateDirectory(_beatmapsetsPath);

		_options = Options.Create(new StorageOptions
		{
			ReplaysPath = "",
			AvatarsPath = "",
			BeatmapsetsPath = _beatmapsetsPath,
			MenuSeasonalsPath = "",
			MenuBannersPath = "",
			FaqsPath = "", CachePath = Path.Combine(_beatmapsetsPath, "Cache")
		});
		var ingestion = new BeatmapIngestionService(_beatmaps, _beatmapsets, new FakeOsuCalculator(), _options,
			new FileSystemResponseCache(_options), new BeatmapsetAssetCache(_options), _ingestionLog);
		_watcher = new BeatmapWatcherService(ingestion, _options, _watcherLog);
	}

	public void Dispose()
	{
		Directory.Delete(_beatmapsetsPath, true);
		// Pooling=False above means there's nothing pooled to clear before deleting.
		File.Delete(_dbPath);
		File.Delete(_dbPath + "-wal");
		File.Delete(_dbPath + "-shm");
	}

	[Fact]
	public async Task DroppingBeatmapsetFolder_GetsAutoIngestedWithinDebounceWindow()
	{
		await _watcher.StartAsync(CancellationToken.None);
		try
		{
			// FileSystemWatcher can silently miss the very first filesystem event after a process's
			// first watcher is armed (a known .NET/Windows cold-start quirk) — a throwaway warm-up
			// event before the real payload avoids that race.
			await File.WriteAllTextAsync(Path.Combine(_beatmapsetsPath, "warmup.txt"), "");
			await Task.Delay(300);
			File.Delete(Path.Combine(_beatmapsetsPath, "warmup.txt"));

			var folder = Path.Combine(_beatmapsetsPath, "900000000 FAIRY FORE - Vivid");
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
	public async Task RenamingBeatmapsetFolderToDeletedMarker_RemovesBeatmapsetWithoutReingestingIt()
	{
		await _watcher.StartAsync(CancellationToken.None);
		try
		{
			await File.WriteAllTextAsync(Path.Combine(_beatmapsetsPath, "warmup.txt"), "");
			await Task.Delay(300);
			File.Delete(Path.Combine(_beatmapsetsPath, "warmup.txt"));

			var folder = Path.Combine(_beatmapsetsPath, "unresolved FAIRY FORE - Vivid");
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
			var resolvedFolder = BeatmapIngestionService.BeatmapsetFolderPath(
				new StorageOptions
				{
					ReplaysPath = "",
					AvatarsPath = "",
					BeatmapsetsPath = _beatmapsetsPath,
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
	public async Task DroppingOsz_SelfDeletingAfterExtraction_DoesNotDeleteTheBeatmapsetItJustIngested()
	{
		await _watcher.StartAsync(CancellationToken.None);
		try
		{
			// FileSystemWatcher can silently miss the very first filesystem event after a process's
			// first watcher is armed (a known .NET/Windows cold-start quirk) — a throwaway warm-up
			// event before the real payload avoids that race.
			await File.WriteAllTextAsync(Path.Combine(_beatmapsetsPath, "warmup.txt"), "");
			await Task.Delay(300);
			File.Delete(Path.Combine(_beatmapsetsPath, "warmup.txt"));

			// Fixture carries an explicit BeatmapSetID (900000) so the beatmapset ResolveBeatmapsetAsync
			// creates has a deterministic id, and the .osz's own filename below uses that same id —
			// matching what a real osu!-downloaded archive looks like ("{setId} Artist - Title.osz").
			// Without that alignment ReconcileDeletedFolderAsync (parsing the leading digits of
			// whatever path it's given) would parse an id that happens not to match any row, and the
			// bug this test exists to catch — deleting the just-ingested beatmapset — would go unnoticed.
			var oszPath = Path.Combine(_beatmapsetsPath, "900000 FAIRY FORE - Vivid.osz");
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

	/// <summary>
	///     Regression test for the live-migration/watcher interaction found on advisor review of
	///     phase 3: <see cref="BeatmapsetMigrationService" /> makes a legacy folder disappear (renamed
	///     into the asset cache, or dropped once its contents are already there) the moment it
	///     publishes that id's canonical ".osz" — with no <see cref="BeatmapIngestionService.DeletedFolderInfix" />
	///     marker, since it isn't a deletion. The live watcher sees that same disappearance and, without
	///     <see cref="BeatmapIngestionService.ReconcileDeletedFolderAsync" />'s canonical-archive guard,
	///     would treat it as the beatmapset being deleted and drop its just-migrated row.
	/// </summary>
	[Fact]
	public async Task MigratingALegacyFolder_WhileTheWatcherIsLive_DoesNotDeleteTheJustMigratedSet()
	{
		await _watcher.StartAsync(CancellationToken.None);
		try
		{
			// FileSystemWatcher can silently miss the very first filesystem event after a process's
			// first watcher is armed (a known .NET/Windows cold-start quirk) — a throwaway warm-up
			// event before the real payload avoids that race.
			await File.WriteAllTextAsync(Path.Combine(_beatmapsetsPath, "warmup.txt"), "");
			await Task.Delay(300);
			File.Delete(Path.Combine(_beatmapsetsPath, "warmup.txt"));

			var folder = Path.Combine(_beatmapsetsPath, "900002 FAIRY FORE - VividLiveMigrate");
			Directory.CreateDirectory(folder);
			File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "vivid_with_setid.osu"),
				Path.Combine(folder, "vivid_with_setid.osu"));
			// vivid_with_setid.osu declares BeatmapSetID:900000, not 900002 -- the folder's own
			// leading-id naming is cosmetic here; ResolveBeatmapsetAsync resolves the real id from the
			// file's online id, matching how DroppingOsz_SelfDeletingAfterExtraction... already relies
			// on the same fixture's embedded id rather than the path.
			var ingestDeadline = DateTime.UtcNow.AddSeconds(10);
			while (DateTime.UtcNow < ingestDeadline &&
			       await _beatmaps.FetchOneAsync(setId: 900000, includePrivate: true) is null)
				await Task.Delay(200);

			var ingested = await _beatmaps.FetchOneAsync(setId: 900000, includePrivate: true);
			Assert.True(ingested is not null,
				"Beatmap never appeared. Ingestion log: " + string.Join(" | ", _ingestionLog.Messages) +
				" || Watcher log: " + string.Join(" | ", _watcherLog.Messages));

			// The watcher renamed the folder to match the resolved id as part of its own ingestion?
			// No -- ReconcileFolderAsync (unlike ReconcileOszAsync) never renames the folder it was
			// given, so the folder is still sitting at its original, off-convention name. Rename it to
			// what BeatmapsetMigrationService's own folder scan expects, matching every other test
			// here that relies on the same lookup.
			var resolvedFolder = BeatmapIngestionService.BeatmapsetFolderPath(_options.Value, ingested!.Beatmapset);
			Directory.Move(folder, resolvedFolder);

			// Run the migration pass live, on the same directory the watcher above is already
			// watching -- this is the scenario under test.
			var migration = new BeatmapsetMigrationService(_beatmapsets, _options,
				new BeatmapsetAssetCache(_options), NullLogger<BeatmapsetMigrationService>.Instance);
			await migration.StartAsync(CancellationToken.None);
			try
			{
				var migrateDeadline = DateTime.UtcNow.AddSeconds(10);
				while (DateTime.UtcNow < migrateDeadline && migration.ExecuteTask is { IsCompleted: false })
					await Task.Delay(50);
			}
			finally
			{
				await migration.StopAsync(CancellationToken.None);
			}

			Assert.NotNull(BeatmapIngestionService.FindBeatmapsetOsz(_options.Value, 900000));

			// Give the watcher's own debounce window (2s) plus margin to process whatever Deleted/
			// Renamed events the migration's folder move fired, then assert the row is still there.
			await Task.Delay(TimeSpan.FromSeconds(4));
			var survived = await _beatmaps.FetchOneAsync(setId: 900000, includePrivate: true);
			Assert.True(survived is not null,
				"Beatmapset was deleted by the live watcher after being successfully migrated. Ingestion log: " +
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