using System.IO.Compression;
using Basil.Application.Configuration;
using Basil.Infrastructure.Beatmaps;
using Basil.Infrastructure.Persistence.Repositories;
using Basil.Infrastructure.Tests.Persistence;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Tests.Beatmaps;

/// <summary>
///     Verifies BeatmapIngestionService against a real SQLite file and the real
///     Fixtures/vivid_osu_file.osu (an old-format file with no BeatmapID/BeatmapSetID fields, so it
///     exercises the local-id-allocation fallback rather than the online-id path).
/// </summary>
public class BeatmapIngestionServiceTests : IClassFixture<SqliteFixture>, IDisposable
{
    private readonly SqliteMapRepository _maps;
    private readonly SqliteMapsetRepository _mapsets;
    private readonly SqliteScoreRepository _scores;
    private readonly string _connectionString;
    private readonly string _mapsetsPath;
    private readonly BeatmapIngestionService _service;

    public BeatmapIngestionServiceTests(SqliteFixture fixture)
    {
        _maps = new SqliteMapRepository(fixture.ConnectionString);
        _mapsets = new SqliteMapsetRepository(fixture.ConnectionString);
        _scores = new SqliteScoreRepository(fixture.ConnectionString);
        _connectionString = fixture.ConnectionString;
        _mapsetsPath = Path.Combine(Path.GetTempPath(), "obt-ingest-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_mapsetsPath);
        _service = new BeatmapIngestionService(_maps, _mapsets, _scores, new FakeDifficultyCalculator(), Options.Create(new StorageOptions
        {
            ReplaysPath = "",
            AvatarsPath = "",
            MapsetsPath = _mapsetsPath,
            SeasonalsPath = "",
            FaqsPath = ""
        }), NullLogger<BeatmapIngestionService>.Instance);
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

        var beatmap = await _maps.FetchOneAsync(setId: setId!.Value);
        Assert.NotNull(beatmap);
        Assert.Equal("vivid_osu_file.osu", beatmap.Filename);
        Assert.Equal("FAIRY FORE", beatmap.Mapset.Artist);
        Assert.Equal("Vivid", beatmap.Mapset.Title);
        Assert.Equal("Insane", beatmap.Version);
        Assert.Equal("Hitoshirenu Shourai", beatmap.Mapset.Creator);
        Assert.True(beatmap.Mapset.Id >= 900_000_000);
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
        Assert.NotNull(await _maps.FetchOneAsync(setId: setId!.Value));
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
        Assert.True(File.Exists(Path.Combine(createdFolder!, "vivid_osu_file.osu")));
        Assert.True(File.Exists(Path.Combine(createdFolder!, "bg.jpg")));
    }

    [Fact]
    public async Task ReconcileDeletedFolderAsync_RemovesMapsetAndBeatmap()
    {
        // ReconcileDeletedFolderAsync parses the Mapset id from the folder's own leading digits, so
        // the folder must be renamed to its actually-resolved id first (a fresh ingestion doesn't
        // reuse whatever number a human happened to type in the folder name).
        var tempFolder = Path.Combine(_mapsetsPath, "unresolved FAIRY FORE - Vivid");
        Directory.CreateDirectory(tempFolder);
        File.Copy(FixtureSourcePath, Path.Combine(tempFolder, "vivid_osu_file.osu"));
        var (_, setId) = await _service.ReconcileFolderAsync(tempFolder);
        Assert.NotNull(setId);

        var mapset = await _mapsets.FetchByIdAsync(setId!.Value);
        Assert.NotNull(mapset);
        var resolvedFolder = BeatmapIngestionService.MapsetFolderPath(
            new StorageOptions { ReplaysPath = "", AvatarsPath = "", MapsetsPath = _mapsetsPath, SeasonalsPath = "", FaqsPath = "" },
            mapset!);
        Directory.Move(tempFolder, resolvedFolder);
        Directory.Delete(resolvedFolder, true);

        await _service.ReconcileDeletedFolderAsync(resolvedFolder);

        Assert.Null(await _mapsets.FetchByIdAsync(setId.Value));
        Assert.Null(await _maps.FetchOneAsync(setId: setId.Value, includeFrozen: true));
    }

    [Fact]
    public async Task ReconcileDeletedFolderAsync_InvalidatesScoresForRemovedBeatmaps()
    {
        var tempFolder = Path.Combine(_mapsetsPath, "unresolved3 FAIRY FORE - Vivid");
        Directory.CreateDirectory(tempFolder);
        File.Copy(FixtureSourcePath, Path.Combine(tempFolder, "vivid_osu_file.osu"));
        var (_, setId) = await _service.ReconcileFolderAsync(tempFolder);
        Assert.NotNull(setId);

        var beatmap = await _maps.FetchOneAsync(setId: setId!.Value);
        Assert.NotNull(beatmap);
        var scoreId = await InsertScoreAsync(beatmap!.Md5);

        var mapset = await _mapsets.FetchByIdAsync(setId.Value);
        var resolvedFolder = BeatmapIngestionService.MapsetFolderPath(
            new StorageOptions { ReplaysPath = "", AvatarsPath = "", MapsetsPath = _mapsetsPath, SeasonalsPath = "", FaqsPath = "" },
            mapset!);
        Directory.Move(tempFolder, resolvedFolder);
        Directory.Delete(resolvedFolder, true);

        await _service.ReconcileDeletedFolderAsync(resolvedFolder);

        Assert.True(await FetchIsInvalidatedAsync(scoreId));
    }

    [Fact]
    public async Task ReconcileFolderAsync_DifficultyRemoved_InvalidatesItsScoresButKeepsOthers()
    {
        var folder = Path.Combine(_mapsetsPath, "900000003 FAIRY FORE - Vivid");
        Directory.CreateDirectory(folder);
        File.Copy(FixtureSourcePath, Path.Combine(folder, "vivid_osu_file.osu"));
        var removedPath = Path.Combine(folder, "vivid_osu_file_hard.osu");
        WriteVariant(removedPath, 3000);

        var (ingestedInFolder, setId) = await _service.ReconcileFolderAsync(folder);
        Assert.Equal(2, ingestedInFolder);

        var keptBeatmap = await _maps.FetchOneAsync(filename: "vivid_osu_file.osu", setId: setId);
        var removedBeatmap = await _maps.FetchOneAsync(filename: "vivid_osu_file_hard.osu", setId: setId);
        Assert.NotNull(keptBeatmap);
        Assert.NotNull(removedBeatmap);
        var removedScoreId = await InsertScoreAsync(removedBeatmap!.Md5);

        File.Delete(removedPath);
        await _service.ReconcileFolderAsync(folder);

        Assert.True(await FetchIsInvalidatedAsync(removedScoreId));
        Assert.Null(await _maps.FetchOneAsync(filename: "vivid_osu_file_hard.osu", setId: setId, includeFrozen: true));
        Assert.NotNull(await _maps.FetchOneAsync(filename: "vivid_osu_file.osu", setId: setId));
    }

    [Fact]
    public async Task ReconcileFolderAsync_ContentChanged_InvalidatesScoresOnOldMd5()
    {
        // Every file's content is about to change (there's only one), so the mapset resolver can't
        // match by content-hash on the second pass and falls back to the folder's own leading-id
        // name — so, like ReconcileDeletedFolderAsync_RemovesMapsetAndBeatmap above, the folder must
        // be renamed to its actually-resolved id first rather than an arbitrary placeholder number.
        var tempFolder = Path.Combine(_mapsetsPath, "unresolved4 FAIRY FORE - Vivid");
        Directory.CreateDirectory(tempFolder);
        File.Copy(FixtureSourcePath, Path.Combine(tempFolder, "vivid_osu_file.osu"));
        var (_, setId) = await _service.ReconcileFolderAsync(tempFolder);
        Assert.NotNull(setId);

        var mapset = await _mapsets.FetchByIdAsync(setId!.Value);
        var folder = BeatmapIngestionService.MapsetFolderPath(
            new StorageOptions { ReplaysPath = "", AvatarsPath = "", MapsetsPath = _mapsetsPath, SeasonalsPath = "", FaqsPath = "" },
            mapset!);
        Directory.Move(tempFolder, folder);
        var osuPath = Path.Combine(folder, "vivid_osu_file.osu");

        var original = await _maps.FetchOneAsync(setId: setId.Value);
        Assert.NotNull(original);
        var oldMd5 = original!.Md5;
        var oldScoreId = await InsertScoreAsync(oldMd5);

        WriteVariant(osuPath, 4000);
        await _service.ReconcileFolderAsync(folder);

        Assert.True(await FetchIsInvalidatedAsync(oldScoreId));
        var updated = await _maps.FetchOneAsync(setId: setId.Value);
        Assert.NotNull(updated);
        Assert.NotEqual(oldMd5, updated!.Md5);
        Assert.Equal(original.Id, updated.Id);
        Assert.Null(await _maps.FetchOneAsync(md5: oldMd5, includeFrozen: true));
    }

    /// <summary>Writes a copy of the fixture .osu with AudioLeadIn tweaked so its content (and md5) differs.</summary>
    private static void WriteVariant(string destPath, int audioLeadIn)
    {
        var text = File.ReadAllText(FixtureSourcePath).Replace("AudioLeadIn: 2000", $"AudioLeadIn: {audioLeadIn}");
        File.WriteAllText(destPath, text);
    }

    private async Task<long> InsertScoreAsync(string mapMd5)
    {
        await using var connection = new SqliteConnection(_connectionString);
        return await connection.ExecuteScalarAsync<long>(
            """
            INSERT INTO Scores (
                MapMd5, Score, Accuracy, MaxCombo, Mods, N300, N100, N50, NMiss, NGeki, NKatu,
                Grade, Mode, PlayTime, TimeElapsed, ClientFlags, UserId, Perfect, OnlineChecksum, SubmittedAt
            ) VALUES (
                @MapMd5, 500000, 95.0, 500, 0, 300, 10, 5, 0, 0, 0,
                'S', 0, datetime('now'), 120000, 0, 1, 0, @Checksum, datetime('now')
            );
            SELECT last_insert_rowid();
            """,
            new { MapMd5 = mapMd5, Checksum = Guid.NewGuid().ToString("N") });
    }

    private async Task<bool> FetchIsInvalidatedAsync(long scoreId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        return await connection.ExecuteScalarAsync<bool>(
            "SELECT IsInvalidated FROM Scores WHERE Id = @Id", new { Id = scoreId });
    }
}
