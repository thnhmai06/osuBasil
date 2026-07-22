using System.Net;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Scores;
using Basil.Application.Configuration;
using Basil.Domain.Beatmaps;
using Basil.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.IntegrationTests;

/// <summary>
///     Covers the public beatmap/mapset routes (info + download + storyboard) that replaced the
///     old public `/beatmaps/{beatmapId}` download and the admin-key-gated `/beatmaps/{id}` JSON
///     lookup — the two were colliding route templates (identical path + verb), which this split
///     onto singular `beatmap`/`mapset` paths resolves. Also covers the MIME-type correctness pass
///     across every download route (osu!'s real per-extension types instead of generic ones).
/// </summary>
public class BeatmapMapsetEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _dataDir = Directory.CreateTempSubdirectory("basil-beatmap-tests-").FullName;
    private readonly StubMapRepository _maps = new();
    private readonly StubScoreRepository _scores = new();
    private readonly StubReplayStorage _replayStorage = new();

    public BeatmapMapsetEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Basil:Server:Domain"] = "test.local",
                    ["Basil:Bot:CommandPrefix"] = "!",
                    ["Basil:Server:MenuIconPath"] = "icon.png",
                    ["Basil:Server:MenuOnclickUrl"] = "https://example.test",
                    ["Basil:Server:AdminKey"] = "correct-key"
                });
            });
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IOptions<DatabaseOptions>>(Options.Create(new DatabaseOptions { Path = "" }));
                services.AddSingleton<IMapRepository>(_maps);
                services.AddSingleton<IScoreRepository>(_scores);
                services.AddSingleton<IReplayStorage>(_replayStorage);
                services.AddSingleton<IOptions<StorageOptions>>(Options.Create(new StorageOptions
                {
                    ReplaysPath = Path.Combine(_dataDir, "Replays"),
                    AvatarsPath = Path.Combine(_dataDir, "Avatars"),
                    MapsetsPath = Path.Combine(_dataDir, "Mapsets"),
                    SeasonalsPath = Path.Combine(_dataDir, "Seasonals"),
                    FaqsPath = Path.Combine(_dataDir, "Faqs")
                }));
            });
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true);
    }

    private static HttpRequestMessage MakeRequest(HttpMethod method, string path, string host = "api.test.local")
    {
        return new HttpRequestMessage(method, path) { Headers = { Host = host } };
    }

    private static Beatmap MakeBeatmap(int id, int setId, string filename = "diff.osu")
    {
        var mapset = new Mapset(setId, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow);
        return new Beatmap(new string('a', 32), id, mapset, "Normal", filename,
            TimeSpan.FromSeconds(100), 500, false, 0, 0,
            new Difficulty(GameMode.Standard, 180, 4, 9, 8, 5, 6.5));
    }

    private string MapsetFolder(int setId)
    {
        var folder = Path.Combine(_dataDir, "Mapsets", $"{setId} Artist - Title");
        Directory.CreateDirectory(folder);
        return folder;
    }

    // ---- GET /beatmap/{id} ----

    [Fact]
    public async Task GetBeatmap_UnknownId_ReturnsNotFound_NoAdminKeyNeeded()
    {
        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/beatmap/999"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetBeatmap_KnownId_ReturnsJsonWithoutAdminKey()
    {
        _maps.OneBeatmap = MakeBeatmap(1, 100);

        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/beatmap/1"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Artist", body);
    }

    // ---- GET /beatmap/{id}/download ----

    [Fact]
    public async Task DownloadBeatmap_UnknownId_ReturnsNotFound()
    {
        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/beatmap/999/download"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DownloadBeatmap_FileMissingOnDisk_ReturnsNotFound()
    {
        _maps.OneBeatmap = MakeBeatmap(1, 100);

        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/beatmap/1/download"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DownloadBeatmap_FileExists_ReturnsCorrectMimeType()
    {
        _maps.OneBeatmap = MakeBeatmap(1, 100, "diff.osu");
        var folder = MapsetFolder(100);
        await File.WriteAllTextAsync(Path.Combine(folder, "diff.osu"), "osu file format v14");

        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/beatmap/1/download"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-osu-beatmap", response.Content.Headers.ContentType?.MediaType);
    }

    // ---- GET /mapset/{id} ----

    [Fact]
    public async Task GetMapset_UnknownId_ReturnsNotFound()
    {
        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/mapset/999"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMapset_KnownId_ReturnsJsonArrayOfBeatmaps()
    {
        _maps.SetBeatmaps = [MakeBeatmap(1, 100, "diff1.osu"), MakeBeatmap(2, 100, "diff2.osu")];

        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/mapset/100"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("diff1.osu", body);
        Assert.Contains("diff2.osu", body);
    }

    // ---- GET /mapset/{id}/download ----

    [Fact]
    public async Task DownloadMapset_NoFolder_ReturnsNotFound()
    {
        _maps.SetBeatmaps = [MakeBeatmap(1, 200)];

        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/mapset/200/download"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DownloadMapset_FolderExists_ReturnsCorrectMimeType()
    {
        _maps.SetBeatmaps = [MakeBeatmap(1, 300, "diff.osu")];
        var folder = MapsetFolder(300);
        await File.WriteAllTextAsync(Path.Combine(folder, "diff.osu"), "osu file format v14");

        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/mapset/300/download"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-osu-beatmap-archive", response.Content.Headers.ContentType?.MediaType);
    }

    // ---- GET /mapset/{id}/storyboard ----

    [Fact]
    public async Task Storyboard_NoFolder_ReturnsNotFound()
    {
        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/mapset/400/storyboard"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Storyboard_FolderExistsNoOsb_ReturnsNotFound()
    {
        MapsetFolder(500);

        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/mapset/500/storyboard"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Storyboard_FolderHasOsbFile_ReturnsCorrectMimeType()
    {
        var folder = MapsetFolder(600);
        await File.WriteAllTextAsync(Path.Combine(folder, "storyboard.osb"), "[Events]");

        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/mapset/600/storyboard"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-osu-storyboard", response.Content.Headers.ContentType?.MediaType);
    }

    // ---- Admin's old single-lookup route is gone (DELETE at the same template still exists —
    // see AdminManagementEndpointTests for DELETE coverage) ----

    [Fact]
    public async Task OldAdminBeatmapLookup_GetNoLongerSupported()
    {
        var request = MakeRequest(HttpMethod.Get, "/beatmaps/1");
        request.Headers.Add("X-Admin-Key", "correct-key");

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- Pre-existing download routes' MIME-type fixes ----

    [Fact]
    public async Task MapFile_Exists_ReturnsCorrectMimeType()
    {
        _maps.ByFilename = MakeBeatmap(1, 700, "Some Map.osu");
        var folder = MapsetFolder(700);
        await File.WriteAllTextAsync(Path.Combine(folder, "Some Map.osu"), "osu file format v14");

        var request = new HttpRequestMessage(HttpMethod.Get, "/web/maps/Some%20Map.osu")
            { Headers = { Host = "osu.test.local" } };
        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-osu-beatmap", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ReplayDownload_Exists_ReturnsCorrectMimeType()
    {
        _scores.Owner = new ScoreOwnerRow(1, GameMode.Standard);
        _replayStorage.Bytes = [1, 2, 3];

        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/replays/1"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-osu-replay", response.Content.Headers.ContentType?.MediaType);
    }

    private sealed class StubMapRepository : IMapRepository
    {
        public Beatmap? OneBeatmap { get; set; }
        public Beatmap? ByFilename { get; set; }
        public IReadOnlyList<Beatmap> SetBeatmaps { get; set; } = [];

        public Task<Beatmap?> FetchOneAsync(int? id = null, string? md5 = null, string? filename = null,
            int? setId = null, bool includeFrozen = false, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(filename is not null ? ByFilename : OneBeatmap);
        }

        public Task<Beatmap> UpsertAsync(Beatmap beatmap, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(beatmap);
        }

        public Task DeleteByMd5Async(string md5, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<IReadOnlyList<Beatmap>>> SearchAsync(string? query, GameMode? mode,
            int offset, int amount, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<IReadOnlyList<Beatmap>>>([]);
        }

        public Task IncrementPlayCountsAsync(int mapId, bool passed, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<int> FetchMaxIdAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }

        public Task UpdateDiffAsync(int id, double diff, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Beatmap>> FetchAllBySetIdAsync(int setId, bool includeFrozen = false,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SetBeatmaps.Count > 0 && SetBeatmaps[0].Mapset.Id == setId
                ? SetBeatmaps
                : (IReadOnlyList<Beatmap>)[]);
        }
    }

    private sealed class StubScoreRepository : IScoreRepository
    {
        public ScoreOwnerRow? Owner { get; set; }

        public Task<long> CreateAsync(ScoreInsertRow row, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0L);
        }

        public Task<bool> ExistsByOnlineChecksumAsync(string onlineChecksum,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task<FirstPlaceScoreRow?> FetchFirstPlaceScoreAsync(string mapMd5, GameMode mode,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<FirstPlaceScoreRow?>(null);
        }

        public Task<ScoreOwnerRow?> FetchOwnerAsync(long scoreId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Owner);
        }

        public Task<IReadOnlyList<RoundScoreRow>> FetchByRoundIdAsync(int roundId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<RoundScoreRow>>([]);
        }
    }

    private sealed class StubReplayStorage : IReplayStorage
    {
        public byte[]? Bytes { get; set; }

        public Task<byte[]?> ReadAsync(long scoreId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Bytes);
        }

        public Task WriteAsync(long scoreId, byte[] data, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
