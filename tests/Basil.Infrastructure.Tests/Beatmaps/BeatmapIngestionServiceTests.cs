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
	private readonly string _beatmapsetsPath;
	private readonly BeatmapIngestionService _service;

	public BeatmapIngestionServiceTests(SqliteFixture fixture)
	{
		_beatmaps = new SqliteBeatmapRepository(fixture.ConnectionString, NullLogger<SqliteBeatmapRepository>.Instance);
		_beatmapsetRepository =
			new SqliteBeatmapsetRepository(fixture.ConnectionString, NullLogger<SqliteBeatmapsetRepository>.Instance);
		_beatmapsetsPath = Path.Combine(Path.GetTempPath(), "obt-ingest-tests-" + Guid.NewGuid());
		Directory.CreateDirectory(_beatmapsetsPath);
		var options = Options.Create(new StorageOptions
		{
			ReplaysPath = "",
			AvatarsPath = "",
			BeatmapsetsPath = _beatmapsetsPath,
			MenuSeasonalsPath = "",
			MenuBannersPath = "",
			FaqsPath = "", CachePath = Path.Combine(_beatmapsetsPath, "Cache")
		});
		_cache = new FileSystemResponseCache(options);
		_service = new BeatmapIngestionService(_beatmaps, _beatmapsetRepository, new FakeOsuCalculator(), options,
			_cache, new BeatmapsetAssetCache(options),
			NullLogger<BeatmapIngestionService>.Instance);
	}

	private static string FixtureSourcePath =>
		Path.Combine(AppContext.BaseDirectory, "Fixtures", "vivid_osu_file.osu");

	public void Dispose()
	{
		Directory.Delete(_beatmapsetsPath, true);
	}

	[Fact]
	public async Task ReconcileAllAsync_LooseOsuFileAtRoot_IsIgnored()
	{
		File.Copy(FixtureSourcePath, Path.Combine(_beatmapsetsPath, "dropped-in-by-admin.osu"));

		var ingested = await _service.ReconcileAllAsync();

		Assert.Equal(0, ingested);
	}

	[Fact]
	public async Task ReconcileAllAsync_BeatmapsetFolder_IngestsBeatmapAndBeatmapset()
	{
		var folder = Path.Combine(_beatmapsetsPath, "900000000 FAIRY FORE - Vivid");
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
		var folder = Path.Combine(_beatmapsetsPath, "900000001 FAIRY FORE - Vivid");
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
	public async Task ReconcileOszAsync_LooseOsz_KeepsCanonicalArchiveAndEagerlyPopulatesTheAssetCache()
	{
		var oszPath = Path.Combine(_beatmapsetsPath, "dropped.osz");
		await using (var archive = await ZipFile.OpenAsync(oszPath, ZipArchiveMode.Create))
		{
			await archive.CreateEntryFromFileAsync(FixtureSourcePath, "vivid_osu_file.osu");
			// The fixture's own [Events] line declares "Chocobos.jpg" as its background -- must match
			// for the preview-background eager-cache step below to find it.
			var dummyEntry = archive.CreateEntry("Chocobos.jpg");
			await using var entryStream = await dummyEntry.OpenAsync();
			await entryStream.WriteAsync("not a real image"u8.ToArray());
		}

		var (ingested, setId) = await _service.ReconcileOszAsync(oszPath);

		Assert.Equal(1, ingested);
		Assert.NotNull(setId);
		// The upload's temp name is moved to the canonical one, not deleted -- ADR-006's ".osz
		// direct storage" design keeps the archive permanently instead of extracting it into a
		// folder and discarding it.
		Assert.False(File.Exists(oszPath));
		var canonicalOsz = Directory.EnumerateFiles(_beatmapsetsPath, "*.osz").SingleOrDefault();
		Assert.NotNull(canonicalOsz);
		Assert.StartsWith(setId!.Value.ToString(), Path.GetFileName(canonicalOsz));
		// "Cache" itself is expected (this fixture roots CachePath under _beatmapsetsPath); no legacy
		// beatmapset folder should exist alongside it.
		Assert.DoesNotContain(Directory.EnumerateDirectories(_beatmapsetsPath), d => Path.GetFileName(d) != "Cache");

		// Eagerly cached at ingest, not extracted into a folder: the .osu (needed for analysis
		// regardless) and the set's own preview background (this single beatmap's background is
		// also the set's preview).
		var setCacheDir = Path.Combine(_beatmapsetsPath, "Cache", "beatmapset-assets", setId.Value.ToString());
		Assert.True(File.Exists(Path.Combine(setCacheDir, "vivid_osu_file.osu")));
		Assert.True(File.Exists(Path.Combine(setCacheDir, "Chocobos.jpg")));
	}

	/// <summary>
	///     Regression test for the orphan-sweep bug found on ADR-006 review: once ingestion treats a
	///     loose .osz as the canonical layout, a set still sitting as a legacy extracted folder (not
	///     yet reached by the background migration pass) must still count as "seen" by
	///     <see cref="BeatmapIngestionService.ReconcileAllAsync" />'s orphan sweep, or it gets
	///     mass-deleted on the very first startup after upgrade.
	/// </summary>
	[Fact]
	public async Task ReconcileAllAsync_MixOfCanonicalOszAndLegacyFolder_OrphanSweepKeepsBoth()
	{
		// A legacy, not-yet-migrated folder-based set, already known from a prior run (mirrors a
		// real deployment: this set was ingested before this session's upgrade, so its row already
		// exists in the DB before the ReconcileAllAsync pass under test below). Genuinely different
		// content (different MD5) from the .osz below, so it resolves to a distinct Beatmapset.
		var legacyFolder = Path.Combine(_beatmapsetsPath, "unresolved FAIRY FORE - VividTwo");
		Directory.CreateDirectory(legacyFolder);
		var legacyContent = (await File.ReadAllTextAsync(FixtureSourcePath)).Replace("Title:Vivid", "Title:VividTwo");
		await File.WriteAllTextAsync(Path.Combine(legacyFolder, "vivid_two.osu"), legacyContent);
		var (_, legacySetId) = await _service.ReconcileFolderAsync(legacyFolder);
		Assert.NotNull(legacySetId);
		// ReconcileFolderAsync doesn't rename the folder to match its resolved id -- rename it here
		// to the convention ReconcileAllAsync's own folder loop expects, matching how a real
		// legacy folder would already be named from its own original ingestion.
		var renamedLegacyFolder = Path.Combine(_beatmapsetsPath, $"{legacySetId} FAIRY FORE - VividTwo");
		Directory.Move(legacyFolder, renamedLegacyFolder);

		// A canonical .osz-based upload, dropped in after the legacy set already existed.
		var oszPath = Path.Combine(_beatmapsetsPath, "dropped.osz");
		await using (var archive = await ZipFile.OpenAsync(oszPath, ZipArchiveMode.Create))
			await archive.CreateEntryFromFileAsync(FixtureSourcePath, "vivid_osu_file.osu");

		var ingested = await _service.ReconcileAllAsync();

		Assert.Equal(2, ingested);
		var oszSetId = int.Parse(Path.GetFileName(Directory.EnumerateFiles(_beatmapsetsPath, "*.osz").Single())
			.Split(' ')[0]);
		Assert.NotNull(await _beatmapsetRepository.FetchByIdAsync(oszSetId));
		// The actual regression being pinned: the legacy set's row must survive this pass' orphan
		// sweep even though it exists only under the legacy layout, not as an .osz.
		Assert.NotNull(await _beatmapsetRepository.FetchByIdAsync(legacySetId!.Value));
		Assert.True(Directory.Exists(renamedLegacyFolder), "the legacy folder is untouched until migration reaches it");
	}

	[Fact]
	public async Task ReconcileDeletedFolderAsync_RemovesBeatmapsetAndBeatmap()
	{
		// ReconcileDeletedFolderAsync parses the Beatmapset id from the folder's own leading digits, so
		// the folder must be renamed to its actually-resolved id first (a fresh ingestion doesn't
		// reuse whatever number a human happened to type in the folder name).
		var tempFolder = Path.Combine(_beatmapsetsPath, "unresolved FAIRY FORE - Vivid");
		Directory.CreateDirectory(tempFolder);
		File.Copy(FixtureSourcePath, Path.Combine(tempFolder, "vivid_osu_file.osu"));
		var (_, setId) = await _service.ReconcileFolderAsync(tempFolder);
		Assert.NotNull(setId);

		var beatmapset = await _beatmapsetRepository.FetchByIdAsync(setId.Value);
		Assert.NotNull(beatmapset);
		var resolvedFolder = BeatmapIngestionService.BeatmapsetFolderPath(
			new StorageOptions
			{
				ReplaysPath = "", AvatarsPath = "", BeatmapsetsPath = _beatmapsetsPath, MenuSeasonalsPath = "",
				MenuBannersPath = "", FaqsPath = "",
				CachePath = ""
			},
			beatmapset);
		Directory.Move(tempFolder, resolvedFolder);
		Directory.Delete(resolvedFolder, true);

		await _service.ReconcileDeletedFolderAsync(resolvedFolder);

		Assert.Null(await _beatmapsetRepository.FetchByIdAsync(setId.Value));
		Assert.Null(await _beatmaps.FetchOneAsync(setId: setId.Value, includePrivate: true));
	}

	[Fact]
	public async Task ReconcileDeletedFolderAsync_InvalidatesThumbAndPreviewCache()
	{
		var tempFolder = Path.Combine(_beatmapsetsPath, "unresolved2 FAIRY FORE - Vivid");
		Directory.CreateDirectory(tempFolder);
		File.Copy(FixtureSourcePath, Path.Combine(tempFolder, "vivid_osu_file.osu"));
		var (_, setId) = await _service.ReconcileFolderAsync(tempFolder);
		Assert.NotNull(setId);

		await _cache.PutAsync("thumb", ResponseCacheKeys.Thumb(setId.Value, false), [1]);
		await _cache.PutAsync("thumb", ResponseCacheKeys.Thumb(setId.Value, true), [1]);
		await _cache.PutAsync("preview", ResponseCacheKeys.Preview(setId.Value), [1]);

		var beatmapset = await _beatmapsetRepository.FetchByIdAsync(setId.Value);
		var resolvedFolder = BeatmapIngestionService.BeatmapsetFolderPath(
			new StorageOptions
			{
				ReplaysPath = "", AvatarsPath = "", BeatmapsetsPath = _beatmapsetsPath, MenuSeasonalsPath = "",
				MenuBannersPath = "", FaqsPath = "",
				CachePath = ""
			},
			beatmapset!);
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
		var folder = Path.Combine(_beatmapsetsPath, "900000003 FAIRY FORE - Vivid");
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
		// name — so, like ReconcileDeletedFolderAsync_RemovesBeatmapsetAndBeatmap above, the folder must
		// be renamed to its actually-resolved id first rather than an arbitrary placeholder number.
		var tempFolder = Path.Combine(_beatmapsetsPath, "unresolved4 FAIRY FORE - Vivid");
		Directory.CreateDirectory(tempFolder);
		File.Copy(FixtureSourcePath, Path.Combine(tempFolder, "vivid_osu_file.osu"));
		var (_, setId) = await _service.ReconcileFolderAsync(tempFolder);
		Assert.NotNull(setId);

		var beatmapset = await _beatmapsetRepository.FetchByIdAsync(setId.Value);
		var folder = BeatmapIngestionService.BeatmapsetFolderPath(
			new StorageOptions
			{
				ReplaysPath = "", AvatarsPath = "", BeatmapsetsPath = _beatmapsetsPath, MenuSeasonalsPath = "",
				MenuBannersPath = "", FaqsPath = "",
				CachePath = ""
			},
			beatmapset!);
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
	public async Task ReconcileFolderAsync_MultipleDifficulties_SetsBeatmapsetPreviewToLowestIdBeatmapsBackground()
	{
		var folder = Path.Combine(_beatmapsetsPath, "900000005 FAIRY FORE - Vivid");
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

		var beatmapset = await _beatmapsetRepository.FetchByIdAsync(setId.Value);
		Assert.Equal(lowest.BackgroundFile, beatmapset!.BackgroundFile);
	}

	[Fact]
	public async Task ReconcileFolderAsync_LowestIdDifficultyRemoved_PreviewFallsBackToNextLowest()
	{
		var folder = Path.Combine(_beatmapsetsPath, "900000006 FAIRY FORE - Vivid");
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

		var beatmapset = await _beatmapsetRepository.FetchByIdAsync(setId.Value);
		Assert.Equal(remaining.BackgroundFile, beatmapset!.BackgroundFile);
	}

	/// <summary>Writes a copy of the fixture .osu with AudioLeadIn tweaked so its content (and md5) differs.</summary>
	private static void WriteVariant(string destPath, int audioLeadIn)
	{
		var text = File.ReadAllText(FixtureSourcePath).Replace("AudioLeadIn: 2000", $"AudioLeadIn: {audioLeadIn}");
		File.WriteAllText(destPath, text);
	}
}