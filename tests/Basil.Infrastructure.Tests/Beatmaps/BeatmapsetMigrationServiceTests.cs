using System.IO.Compression;
using Basil.Application.Configurations;
using Basil.Infrastructure.Beatmaps;
using Basil.Infrastructure.Persistence;
using Basil.Infrastructure.Persistence.Repositories;
using Basil.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Tests.Beatmaps;

/// <summary>
///     Covers <see cref="BeatmapsetMigrationService" /> against a real temp filesystem and a real
///     SQLite file: converting a legacy extracted folder to the canonical ".osz" layout, the cheap
///     skip check for an already-migrated set, crash-midway recovery (a stale ".osz.tmp" left by an
///     interrupted prior pass), and the cross-volume fallback for the asset-cache pre-warm move.
/// </summary>
[Collection(BeatmapFilesystemTestCollection.Name)]
public sealed class BeatmapsetMigrationServiceTests : IDisposable
{
	private readonly SqliteBeatmapRepository _beatmaps;
	private readonly SqliteBeatmapsetRepository _beatmapsets;
	private readonly string _beatmapsetsPath;
	private readonly string _cachePath;
	private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"basil-migration-test-{Guid.NewGuid():N}.db");
	private readonly BeatmapIngestionService _ingestion;

	public BeatmapsetMigrationServiceTests()
	{
		var connectionString = $"Data Source={_dbPath};Foreign Keys=True;Default Timeout=5;Pooling=False";
		SqlMigrationRunner.RunMigrations(connectionString);

		_beatmaps = new SqliteBeatmapRepository(connectionString, NullLogger<SqliteBeatmapRepository>.Instance);
		_beatmapsets =
			new SqliteBeatmapsetRepository(connectionString, NullLogger<SqliteBeatmapsetRepository>.Instance);
		_beatmapsetsPath = Path.Combine(Path.GetTempPath(), "obt-migration-tests-" + Guid.NewGuid());
		Directory.CreateDirectory(_beatmapsetsPath);
		_cachePath = Path.Combine(_beatmapsetsPath, "Cache");

		var options = Options.Create(new StorageOptions
		{
			ReplaysPath = "",
			AvatarsPath = "",
			BeatmapsetsPath = _beatmapsetsPath,
			MenuSeasonalsPath = "",
			MenuBannersPath = "",
			FaqsPath = "", CachePath = _cachePath
		});
		_ingestion = new BeatmapIngestionService(_beatmaps, _beatmapsets, new FakeOsuCalculator(), options,
			new FileSystemResponseCache(options), new BeatmapsetAssetCache(options),
			NullLogger<BeatmapIngestionService>.Instance);
	}

	public void Dispose()
	{
		Directory.Delete(_beatmapsetsPath, true);
		File.Delete(_dbPath);
		File.Delete(_dbPath + "-wal");
		File.Delete(_dbPath + "-shm");
	}

	private static string FixtureSourcePath =>
		Path.Combine(AppContext.BaseDirectory, "Fixtures", "vivid_osu_file.osu");

	private BeatmapsetMigrationService MakeService(string cachePath)
	{
		var options = Options.Create(new StorageOptions
		{
			ReplaysPath = "",
			AvatarsPath = "",
			BeatmapsetsPath = _beatmapsetsPath,
			MenuSeasonalsPath = "",
			MenuBannersPath = "",
			FaqsPath = "", CachePath = cachePath
		});
		return new BeatmapsetMigrationService(_beatmapsets, options, new BeatmapsetAssetCache(options),
			NullLogger<BeatmapsetMigrationService>.Instance);
	}

	/// <summary>Ingests a legacy folder and renames it to the convention the migration pass expects.</summary>
	private async Task<int> SeedLegacyFolderAsync(string folderName, string title)
	{
		var folder = Path.Combine(_beatmapsetsPath, folderName);
		Directory.CreateDirectory(folder);
		var content = (await File.ReadAllTextAsync(FixtureSourcePath)).Replace("Title:Vivid", $"Title:{title}");
		await File.WriteAllTextAsync(Path.Combine(folder, $"{title}.osu"), content);

		var (_, setId) = await _ingestion.ReconcileFolderAsync(folder);
		Assert.NotNull(setId);
		var renamed = Path.Combine(_beatmapsetsPath, $"{setId} {folderName}");
		Directory.Move(folder, renamed);
		return setId!.Value;
	}

	private static async Task RunToCompletionAsync(BeatmapsetMigrationService service)
	{
		await service.StartAsync(CancellationToken.None);
		try
		{
			// The pass runs one shot to completion inside ExecuteAsync's background Task; poll for
			// its own IsCompleted rather than a fixed sleep, matching this test suite's other
			// hosted-service tests.
			var deadline = DateTime.UtcNow.AddSeconds(10);
			while (DateTime.UtcNow < deadline && service.ExecuteTask is { IsCompleted: false })
				await Task.Delay(50);
		}
		finally
		{
			await service.StopAsync(CancellationToken.None);
		}
	}

	[Fact]
	public async Task MigratesLegacyFolder_BuildsCanonicalOszAndPreWarmsAssetCache()
	{
		var setId = await SeedLegacyFolderAsync("FAIRY FORE - VividMigrate", "VividMigrate");

		await RunToCompletionAsync(MakeService(_cachePath));

		var canonicalOsz = BeatmapIngestionService.FindBeatmapsetOsz(
			new StorageOptions
			{
				ReplaysPath = "", AvatarsPath = "", BeatmapsetsPath = _beatmapsetsPath, MenuSeasonalsPath = "",
				MenuBannersPath = "", FaqsPath = "", CachePath = _cachePath
			}, setId);
		Assert.NotNull(canonicalOsz);
		Assert.True(File.Exists(canonicalOsz));

		// The legacy folder is gone -- its own leading-id-named directory no longer exists under
		// BeatmapsetsPath.
		Assert.DoesNotContain(Directory.EnumerateDirectories(_beatmapsetsPath),
			d => Path.GetFileName(d) != "Cache" && Path.GetFileName(d).StartsWith($"{setId} "));

		// Pre-warmed via a folder rename, not re-extracted from the archive: the .osu file that was
		// directly inside the legacy folder is now directly inside the asset cache's set directory.
		var cacheSetDir = Path.Combine(_cachePath, "beatmapset-assets", setId.ToString());
		Assert.True(File.Exists(Path.Combine(cacheSetDir, "VividMigrate.osu")));

		// The DB row is untouched by migration -- only the on-disk layout changed.
		Assert.NotNull(await _beatmaps.FetchOneAsync(setId: setId, includePrivate: true));
	}

	[Fact]
	public async Task AlreadyMigratedSet_SkipsWithoutTouchingTheStrayFolder()
	{
		var setId = await SeedLegacyFolderAsync("FAIRY FORE - VividSkip", "VividSkip");

		// Simulate "already migrated": a canonical .osz already exists for this id (built directly,
		// bypassing the migration pass), while the legacy folder is somehow still present too.
		var beatmapset = await _beatmapsets.FetchByIdAsync(setId);
		var canonicalOszPath = BeatmapIngestionService.BeatmapsetOszPath(
			new StorageOptions
			{
				ReplaysPath = "", AvatarsPath = "", BeatmapsetsPath = _beatmapsetsPath, MenuSeasonalsPath = "",
				MenuBannersPath = "", FaqsPath = "", CachePath = _cachePath
			}, beatmapset!);
		await using (File.Create(canonicalOszPath))
		{
			// empty placeholder archive is enough to exercise the skip check
		}

		var strayFolder = Path.Combine(_beatmapsetsPath, $"{setId} FAIRY FORE - VividSkip");
		Assert.True(Directory.Exists(strayFolder));

		await RunToCompletionAsync(MakeService(_cachePath));

		// The skip check returns before any folder mutation, so the stray folder is left exactly as
		// it was rather than being deleted or merged.
		Assert.True(Directory.Exists(strayFolder));
		Assert.True(File.Exists(Path.Combine(strayFolder, "VividSkip.osu")));
	}

	[Fact]
	public async Task StaleTempArchiveFromAnInterruptedPass_DoesNotBlockTheRetry()
	{
		var setId = await SeedLegacyFolderAsync("FAIRY FORE - VividCrash", "VividCrash");

		var beatmapset = await _beatmapsets.FetchByIdAsync(setId);
		var canonicalOszPath = BeatmapIngestionService.BeatmapsetOszPath(
			new StorageOptions
			{
				ReplaysPath = "", AvatarsPath = "", BeatmapsetsPath = _beatmapsetsPath, MenuSeasonalsPath = "",
				MenuBannersPath = "", FaqsPath = "", CachePath = _cachePath
			}, beatmapset!);
		// A prior pass crashed after starting the archive build but before the rename -- leaves a
		// corrupt/incomplete ".osz.tmp" behind. This must not match FindBeatmapsetOsz's "*.osz" glob,
		// so the skip check still treats the set as not-yet-migrated.
		await File.WriteAllTextAsync(canonicalOszPath + ".tmp", "not a real archive");

		await RunToCompletionAsync(MakeService(_cachePath));

		Assert.True(File.Exists(canonicalOszPath));
		await using (var archive = await ZipFile.OpenReadAsync(canonicalOszPath))
			Assert.Contains(archive.Entries, e => e.Name == "VividCrash.osu");
		Assert.False(File.Exists(canonicalOszPath + ".tmp"));
	}

	[Fact]
	public async Task CachePathOnADifferentVolumeThanBeatmapsetsPath_FallsBackToCopyInsteadOfFailing()
	{
		var setId = await SeedLegacyFolderAsync("FAIRY FORE - VividCrossVolume", "VividCrossVolume");

		// BeatmapsetsPath lives under Path.GetTempPath() (this machine's system temp volume);
		// CachePath here is deliberately pinned to a different volume so Directory.Move's fast
		// rename path throws and the copy+delete fallback must run instead.
		var crossVolumeCache = Path.Combine("V:\\", "tmp-basil-migration-crossvolume-" + Guid.NewGuid());
		Directory.CreateDirectory(crossVolumeCache);
		try
		{
			if (string.Equals(Path.GetPathRoot(_beatmapsetsPath), Path.GetPathRoot(crossVolumeCache),
				    StringComparison.OrdinalIgnoreCase))
				return; // this machine has no second volume to test against; nothing to assert

			await RunToCompletionAsync(MakeService(crossVolumeCache));

			var cacheSetDir = Path.Combine(crossVolumeCache, "beatmapset-assets", setId.ToString());
			Assert.True(File.Exists(Path.Combine(cacheSetDir, "VividCrossVolume.osu")));
			Assert.False(Directory.Exists(Path.Combine(_beatmapsetsPath, $"{setId} FAIRY FORE - VividCrossVolume")));
		}
		finally
		{
			Directory.Delete(crossVolumeCache, true);
		}
	}
}