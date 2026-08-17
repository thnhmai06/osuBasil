using System.IO.Compression;
using Basil.Application.Abstractions.Storage;
using Basil.Application.Configurations;
using Basil.Infrastructure.Beatmaps;
using Basil.Infrastructure.Persistence.Repositories;
using Basil.Infrastructure.Storage;
using Basil.Infrastructure.Tests.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Tests.Beatmaps;

/// <summary>
///     Verifies BeatmapIngestionService against a real SQLite file and the real
///     Fixtures/vivid_osu_file.osu (an old-format file with no BeatmapID/BeatmapSetID fields, so it
///     exercises the local-id-allocation fallback rather than the online-id path).
/// </summary>
[Collection(BeatmapFilesystemTestCollection.Name)]
public class BeatmapIngestionServiceTests : IClassFixture<SqliteFixture>, IDisposable
{
	private readonly SqliteBeatmapRepository _beatmaps;
	private readonly SqliteBeatmapsetRepository _beatmapsetRepository;
	private readonly IResponseCache _cache;
	private readonly string _mapsetsPath;
	private readonly BeatmapIngestionService _service;

	public BeatmapIngestionServiceTests(SqliteFixture fixture)
	{
		_beatmaps = new SqliteBeatmapRepository(fixture.ConnectionString, NullLogger<SqliteBeatmapRepository>.Instance);
		_beatmapsetRepository =
			new SqliteBeatmapsetRepository(fixture.ConnectionString, NullLogger<SqliteBeatmapsetRepository>.Instance);
		_mapsetsPath = Path.Combine(Path.GetTempPath(), "obt-ingest-tests-" + Guid.NewGuid());
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
		_cache = new FileSystemResponseCache(options);
		_service = new BeatmapIngestionService(_beatmaps, _beatmapsetRepository, new FakeOsuCalculator(), options,
			_cache,
			NullLogger<BeatmapIngestionService>.Instance);
	}

	private static string FixtureSourcePath =>
		Path.Combine(AppContext.BaseDirectory, "Fixtures", "vivid_osu_file.osu");

	public void Dispose()
	{
		Directory.Delete(_mapsetsPath, true);
	}

	[Fact]
	public async Task ReconcileAllAsync_LooseOsuFileAtRoot_IsIgnored()
	{
		File.Copy(FixtureSourcePath, Path.Combine(_mapsetsPath, "dropped-in-by-admin.osu"));

		var ingested = await _service.ReconcileAllAsync();

		Assert.Equal(0, ingested);
	}

	[Fact]
	public async Task ReconcileAllAsync_MapsetFolder_IngestsBeatmapAndMapset()
	{
		var folder = Path.Combine(_mapsetsPath, "900000000 FAIRY FORE - Vivid");
		Directory.CreateDirectory(folder);
		File.Copy(FixtureSourcePath, Path.Combine(folder, "vivid_osu_file.osu"));

		var (ingestedInFolder, setId) = await _service.ReconcileFolderAsync(folder);

		Assert.Equal(1, ingestedInFolder);
		Assert.NotNull(setId);

		var beatmap = await _beatmaps.FetchOneAsync(setId: setId.Value);
		Assert.NotNull(beatmap);
		Assert.Equal("vivid_osu_file.osu", beatmap.Filename);
		Assert.Equal("FAIRY FORE", beatmap.Beatmapset.Artist);
		Assert.Equal("Vivid", beatmap.Beatmapset.Title);
		Assert.Equal("Insane", beatmap.Version);
		Assert.Equal("Hitoshirenu Shourai", beatmap.Beatmapset.Creator);
		Assert.True(beatmap.Beatmapset.Id >= 900_000_000);
	}

	[Fact]
	public async Task ReconcileFolderAsync_UnchangedFolder_ReingestsSameRowWithSameId()
	{
		var folder = Path.Combine(_mapsetsPath, "900000001 FAIRY FORE - Vivid");
		Directory.CreateDirectory(folder);
		File.Copy(FixtureSourcePath, Path.Combine(folder, "vivid_osu_file.osu"));

		var (firstCount, setId) = await _service.ReconcileFolderAsync(folder);
		Assert.Equal(1, firstCount);
		Assert.NotNull(setId);

		var (secondCount, secondSetId) = await _service.ReconcileFolderAsync(folder);

		Assert.Equal(1, secondCount);
		Assert.Equal(setId, secondSetId);
		Assert.NotNull(await _beatmaps.FetchOneAsync(setId: setId.Value));
	}

	[Fact]
	public async Task ReconcileAllAsync_LooseOsz_ExtractsFullContentsAndDeletesArchive()
	{
		var oszPath = Path.Combine(_mapsetsPath, "dropped.osz");
		await using (var archive = await ZipFile.OpenAsync(oszPath, ZipArchiveMode.Create))
		{
			await archive.CreateEntryFromFileAsync(FixtureSourcePath, "vivid_osu_file.osu");
			var dummyEntry = archive.CreateEntry("bg.jpg");
			await using var entryStream = await dummyEntry.OpenAsync();
			await entryStream.WriteAsync("not a real image"u8.ToArray());
		}

		var ingested = await _service.ReconcileAllAsync();

		Assert.Equal(1, ingested);
		Assert.False(File.Exists(oszPath));

		var createdFolder = Directory.EnumerateDirectories(_mapsetsPath).FirstOrDefault();
		Assert.NotNull(createdFolder);
		Assert.True(File.Exists(Path.Combine(createdFolder, "vivid_osu_file.osu")));
		Assert.True(File.Exists(Path.Combine(createdFolder, "bg.jpg")));
	}

	[Fact]
	public async Task ReconcileDeletedFolderAsync_RemovesMapsetAndBeatmap()
	{
		// ReconcileDeletedFolderAsync parses the Beatmapset id from the folder's own leading digits, so
		// the folder must be renamed to its actually-resolved id first (a fresh ingestion doesn't
		// reuse whatever number a human happened to type in the folder name).
		var tempFolder = Path.Combine(_mapsetsPath, "unresolved FAIRY FORE - Vivid");
		Directory.CreateDirectory(tempFolder);
		File.Copy(FixtureSourcePath, Path.Combine(tempFolder, "vivid_osu_file.osu"));
		var (_, setId) = await _service.ReconcileFolderAsync(tempFolder);
		Assert.NotNull(setId);

		var mapset = await _beatmapsetRepository.FetchByIdAsync(setId.Value);
		Assert.NotNull(mapset);
		var resolvedFolder = BeatmapIngestionService.MapsetFolderPath(
			new StorageOptions
			{
				ReplaysPath = "", AvatarsPath = "", MapsetsPath = _mapsetsPath, MenuSeasonalsPath = "", MenuBannersPath = "", FaqsPath = "",
				CachePath = ""
			},
			mapset);
		Directory.Move(tempFolder, resolvedFolder);
		Directory.Delete(resolvedFolder, true);

		await _service.ReconcileDeletedFolderAsync(resolvedFolder);

		Assert.Null(await _beatmapsetRepository.FetchByIdAsync(setId.Value));
		Assert.Null(await _beatmaps.FetchOneAsync(setId: setId.Value, includePrivate: true));
	}

	[Fact]
	public async Task ReconcileDeletedFolderAsync_InvalidatesThumbAndPreviewCache()
	{
		var tempFolder = Path.Combine(_mapsetsPath, "unresolved2 FAIRY FORE - Vivid");
		Directory.CreateDirectory(tempFolder);
		File.Copy(FixtureSourcePath, Path.Combine(tempFolder, "vivid_osu_file.osu"));
		var (_, setId) = await _service.ReconcileFolderAsync(tempFolder);
		Assert.NotNull(setId);

		await _cache.PutAsync("thumb", ResponseCacheKeys.Thumb(setId.Value, false), [1]);
		await _cache.PutAsync("thumb", ResponseCacheKeys.Thumb(setId.Value, true), [1]);
		await _cache.PutAsync("preview", ResponseCacheKeys.Preview(setId.Value), [1]);

		var mapset = await _beatmapsetRepository.FetchByIdAsync(setId.Value);
		var resolvedFolder = BeatmapIngestionService.MapsetFolderPath(
			new StorageOptions
			{
				ReplaysPath = "", AvatarsPath = "", MapsetsPath = _mapsetsPath, MenuSeasonalsPath = "", MenuBannersPath = "", FaqsPath = "",
				CachePath = ""
			},
			mapset!);
		Directory.Move(tempFolder, resolvedFolder);
		Directory.Delete(resolvedFolder, true);

		await _service.ReconcileDeletedFolderAsync(resolvedFolder);

		Assert.Null(await _cache.GetAsync("thumb", ResponseCacheKeys.Thumb(setId.Value, false)));
		Assert.Null(await _cache.GetAsync("thumb", ResponseCacheKeys.Thumb(setId.Value, true)));
		Assert.Null(await _cache.GetAsync("preview", ResponseCacheKeys.Preview(setId.Value)));
	}

	[Fact]
	public async Task ReconcileFolderAsync_DifficultyRemoved_DeletesItButKeepsOthers()
	{
		var folder = Path.Combine(_mapsetsPath, "900000003 FAIRY FORE - Vivid");
		Directory.CreateDirectory(folder);
		File.Copy(FixtureSourcePath, Path.Combine(folder, "vivid_osu_file.osu"));
		var removedPath = Path.Combine(folder, "vivid_osu_file_hard.osu");
		WriteVariant(removedPath, 3000);

		var (ingestedInFolder, setId) = await _service.ReconcileFolderAsync(folder);
		Assert.Equal(2, ingestedInFolder);

		var keptBeatmap = await _beatmaps.FetchOneAsync(filename: "vivid_osu_file.osu", setId: setId);
		var removedBeatmap = await _beatmaps.FetchOneAsync(filename: "vivid_osu_file_hard.osu", setId: setId);
		Assert.NotNull(keptBeatmap);
		Assert.NotNull(removedBeatmap);

		File.Delete(removedPath);
		await _service.ReconcileFolderAsync(folder);

		Assert.Null(await _beatmaps.FetchOneAsync(filename: "vivid_osu_file_hard.osu", setId: setId,
			includePrivate: true));
		Assert.NotNull(await _beatmaps.FetchOneAsync(filename: "vivid_osu_file.osu", setId: setId));
	}

	[Fact]
	public async Task ReconcileFolderAsync_ContentChanged_MovesOntoNewMd5ButKeepsId()
	{
		// Every file's content is about to change (there's only one), so the beatmapset resolver can't
		// match by content-hash on the second pass and falls back to the folder's own leading-id
		// name — so, like ReconcileDeletedFolderAsync_RemovesMapsetAndBeatmap above, the folder must
		// be renamed to its actually-resolved id first rather than an arbitrary placeholder number.
		var tempFolder = Path.Combine(_mapsetsPath, "unresolved4 FAIRY FORE - Vivid");
		Directory.CreateDirectory(tempFolder);
		File.Copy(FixtureSourcePath, Path.Combine(tempFolder, "vivid_osu_file.osu"));
		var (_, setId) = await _service.ReconcileFolderAsync(tempFolder);
		Assert.NotNull(setId);

		var mapset = await _beatmapsetRepository.FetchByIdAsync(setId.Value);
		var folder = BeatmapIngestionService.MapsetFolderPath(
			new StorageOptions
			{
				ReplaysPath = "", AvatarsPath = "", MapsetsPath = _mapsetsPath, MenuSeasonalsPath = "", MenuBannersPath = "", FaqsPath = "",
				CachePath = ""
			},
			mapset!);
		Directory.Move(tempFolder, folder);
		var osuPath = Path.Combine(folder, "vivid_osu_file.osu");

		var original = await _beatmaps.FetchOneAsync(setId: setId.Value);
		Assert.NotNull(original);
		var oldMd5 = original.Md5;

		WriteVariant(osuPath, 4000);
		await _service.ReconcileFolderAsync(folder);

		// This is the exact mechanism the api. host's nullable Beatmap embeds rely on: once content
		// changes, the old md5 permanently stops resolving (a stale round/score's reference goes
		// "null beatmap"), while the row's own Id is preserved across the content change.
		var updated = await _beatmaps.FetchOneAsync(setId: setId.Value);
		Assert.NotNull(updated);
		Assert.NotEqual(oldMd5, updated.Md5);
		Assert.Equal(original.Id, updated.Id);
		Assert.Null(await _beatmaps.FetchOneAsync(md5: oldMd5, includePrivate: true));
	}

	[Fact]
	public async Task ReconcileFolderAsync_MultipleDifficulties_SetsMapsetPreviewToLowestIdBeatmapsBackground()
	{
		var folder = Path.Combine(_mapsetsPath, "900000005 FAIRY FORE - Vivid");
		Directory.CreateDirectory(folder);
		File.Copy(FixtureSourcePath, Path.Combine(folder, "vivid_osu_file.osu"));
		var secondPath = Path.Combine(folder, "vivid_osu_file_hard.osu");
		WriteVariant(secondPath, 3000);
		// A distinct background on the second difficulty lets the assertion tell which one actually
		// won, instead of assuming file-enumeration order.
		await File.WriteAllTextAsync(secondPath,
			(await File.ReadAllTextAsync(secondPath)).Replace("Chocobos.jpg", "Moogle.jpg"));

		var (ingestedInFolder, setId) = await _service.ReconcileFolderAsync(folder);
		Assert.Equal(2, ingestedInFolder);
		Assert.NotNull(setId);

		var beatmaps = await _beatmaps.FetchAllBySetIdAsync(setId.Value, true);
		var lowest = beatmaps.MinBy(b => b.Id)!;

		var mapset = await _beatmapsetRepository.FetchByIdAsync(setId.Value);
		Assert.Equal(lowest.BackgroundFile, mapset!.BackgroundFile);
	}

	[Fact]
	public async Task ReconcileFolderAsync_LowestIdDifficultyRemoved_PreviewFallsBackToNextLowest()
	{
		var folder = Path.Combine(_mapsetsPath, "900000006 FAIRY FORE - Vivid");
		Directory.CreateDirectory(folder);
		File.Copy(FixtureSourcePath, Path.Combine(folder, "vivid_osu_file.osu"));
		var secondPath = Path.Combine(folder, "vivid_osu_file_hard.osu");
		WriteVariant(secondPath, 3000);
		await File.WriteAllTextAsync(secondPath,
			(await File.ReadAllTextAsync(secondPath)).Replace("Chocobos.jpg", "Moogle.jpg"));

		var (_, setId) = await _service.ReconcileFolderAsync(folder);
		Assert.NotNull(setId);

		var beatmaps = await _beatmaps.FetchAllBySetIdAsync(setId.Value, true);
		var lowest = beatmaps.MinBy(b => b.Id)!;
		var remaining = beatmaps.First(b => b.Id != lowest.Id);

		File.Delete(Path.Combine(folder, lowest.Filename));
		await _service.ReconcileFolderAsync(folder);

		var mapset = await _beatmapsetRepository.FetchByIdAsync(setId.Value);
		Assert.Equal(remaining.BackgroundFile, mapset!.BackgroundFile);
	}

	/// <summary>Writes a copy of the fixture .osu with AudioLeadIn tweaked so its content (and md5) differs.</summary>
	private static void WriteVariant(string destPath, int audioLeadIn)
	{
		var text = File.ReadAllText(FixtureSourcePath).Replace("AudioLeadIn: 2000", $"AudioLeadIn: {audioLeadIn}");
		File.WriteAllText(destPath, text);
	}
}