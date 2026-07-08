using Basil.Application.Configuration;
using Basil.Domain.Beatmaps;
using Basil.Infrastructure.Beatmaps;
using Basil.Infrastructure.Persistence.Repositories;
using Basil.Infrastructure.Tests.Persistence;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Tests.Beatmaps;

/// <summary>
///     Verifies BeatmapIngestionService against a real MySQL instance and the real
///     Fixtures/vivid_osu_file.osu (an old-format file with no BeatmapID/BeatmapSetID fields, so it
///     exercises the local-id-allocation fallback rather than the online-id path).
/// </summary>
public class BeatmapIngestionServiceTests : IClassFixture<MySqlFixture>, IDisposable
{
    private readonly MySqlMapRepository _maps;
    private readonly MySqlMapsetRepository _mapsets;
    private readonly string _mapsetsPath;
    private readonly BeatmapIngestionService _service;

    public BeatmapIngestionServiceTests(MySqlFixture fixture)
    {
        _maps = new MySqlMapRepository(fixture.ConnectionString);
        _mapsets = new MySqlMapsetRepository(fixture.ConnectionString);
        _mapsetsPath = Path.Combine(Path.GetTempPath(), "obt-ingest-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_mapsetsPath);
        _service = new BeatmapIngestionService(_maps, _mapsets, Options.Create(new StorageOptions
        {
            MapsetsPath = _mapsetsPath
        }));
    }

    private static string FixtureSourcePath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "vivid_osu_file.osu");

    public void Dispose()
    {
        Directory.Delete(_mapsetsPath, true);
    }

    [Fact]
    public async Task IngestAsync_LooseOsuFileWithNoOnlineId_RegistersUnderAllocatedLocalId()
    {
        File.Copy(FixtureSourcePath, Path.Combine(_mapsetsPath, "dropped-in-by-admin.osu"));

        var ingested = await _service.IngestAsync();

        Assert.Equal(1, ingested);

        var beatmap = await _maps.FetchOneAsync(900_000_000);
        Assert.NotNull(beatmap);
        Assert.Equal("FAIRY FORE", beatmap!.Artist);
        Assert.Equal("Vivid", beatmap.Title);
        Assert.Equal("Insane", beatmap.Version);
        Assert.Equal("Hitoshirenu Shourai", beatmap.Creator);
        Assert.Equal(900_000_000, beatmap.SetId);
        Assert.Equal(RankedStatus.Ranked, beatmap.Status);
        Assert.True(File.Exists(Path.Combine(_mapsetsPath, "900000000.osu")));
        Assert.False(File.Exists(Path.Combine(_mapsetsPath, "dropped-in-by-admin.osu")));
    }

    [Fact]
    public async Task IngestAsync_CanonicallyNamedFile_IsSkippedOnRescan()
    {
        File.Copy(FixtureSourcePath, Path.Combine(_mapsetsPath, "900000000.osu"));

        var ingested = await _service.IngestAsync();

        Assert.Equal(0, ingested);
    }
}