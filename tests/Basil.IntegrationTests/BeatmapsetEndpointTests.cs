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
///     Covers the public `/beatmapsets` routes (info + downloads) — the old singular `/beatmap/{id}`
///     and `/beatmap/{id}/download` routes were dropped in favor of `GET /beatmapsets/{mapsetId}`
///     (which now embeds each beatmap's id/version/mode inline), `GET
///     /beatmapsets/{mapsetId}/{beatmapId}` (a single difficulty's JSON metadata), and `GET
///     /beatmapsets/{mapsetId}/{beatmapId}/download` (the raw `.osu` file, moved off the bare
///     path). Also covers the MIME-type correctness pass across every download route (osu!'s real
///     per-extension types instead of generic ones).
/// </summary>
public class BeatmapsetEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _dataDir = Directory.CreateTempSubdirectory("basil-beatmap-tests-").FullName;
    private readonly StubMapRepository _maps = new();
    private readonly StubMapsetRepository _mapsets = new();
    private readonly StubScoreRepository _scores = new();
    private readonly StubReplayStorage _replayStorage = new();

    public BeatmapsetEndpointTests(WebApplicationFactory<Program> factory)
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
                services.AddSingleton<IMapsetRepository>(_mapsets);
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

    private static Mapset MakeMapset(int id)
    {
        return new Mapset(id, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow);
    }

    private static Beatmap MakeBeatmap(int id, Mapset mapset, string filename = "diff.osu")
    {
        return new Beatmap(new string('a', 32), id, mapset, "Normal", filename,
            TimeSpan.FromSeconds(100), 500, 0, 0,
            new Difficulty(GameMode.Standard, 180, 4, 9, 8, 5, 6.5));
    }

    private string MapsetFolder(int setId)
    {
        var folder = Path.Combine(_dataDir, "Mapsets", $"{setId} Artist - Title");
        Directory.CreateDirectory(folder);
        return folder;
    }

    // ---- GET /beatmapsets/{mapsetId} ----

    [Fact]
    public async Task GetBeatmapset_UnknownId_ReturnsNotFound()
    {
        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/999"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetBeatmapset_KnownId_ReturnsInfoWithBeatmapsInline()
    {
        var mapset = MakeMapset(100);
        _mapsets.Mapset = mapset;
        _maps.SetBeatmaps = [MakeBeatmap(1, mapset, "diff1.osu"), MakeBeatmap(2, mapset, "diff2.osu")];

        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/100"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"artist\":\"Artist\"", body);
        Assert.Contains("\"beatmaps\"", body);
    }

    [Fact]
    public async Task GetBeatmapset_Private_NonAdmin_ReturnsNotFound()
    {
        var mapset = MakeMapset(101) with { IsPrivate = true };
        _mapsets.Mapset = mapset;
        _maps.SetBeatmaps = [MakeBeatmap(1, mapset)];

        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/101"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- GET /beatmapsets/{mapsetId}/{beatmapId} (info) ----

    [Fact]
    public async Task BeatmapInfo_UnknownId_ReturnsNotFound()
    {
        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/100/999"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task BeatmapInfo_KnownId_ReturnsJson()
    {
        var mapset = MakeMapset(100);
        _maps.OneBeatmap = MakeBeatmap(1, mapset, "diff.osu");

        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/100/1"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"version\":\"Normal\"", body);
        Assert.Contains("\"filename\":\"diff.osu\"", body);
    }

    // ---- GET /beatmapsets/{mapsetId}/{beatmapId}/download ----

    [Fact]
    public async Task DownloadBeatmap_UnknownId_ReturnsNotFound()
    {
        var response = await _factory.CreateClient()
            .SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/100/999/download"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DownloadBeatmap_FileMissingOnDisk_ReturnsNotFound()
    {
        var mapset = MakeMapset(100);
        _maps.OneBeatmap = MakeBeatmap(1, mapset);

        var response = await _factory.CreateClient()
            .SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/100/1/download"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DownloadBeatmap_FileExists_ReturnsCorrectMimeType()
    {
        var mapset = MakeMapset(100);
        _maps.OneBeatmap = MakeBeatmap(1, mapset, "diff.osu");
        var folder = MapsetFolder(100);
        await File.WriteAllTextAsync(Path.Combine(folder, "diff.osu"), "osu file format v14");

        var response = await _factory.CreateClient()
            .SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/100/1/download"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-osu-beatmap", response.Content.Headers.ContentType?.MediaType);
    }

    // ---- GET /beatmapsets/{mapsetId}/download ----

    [Fact]
    public async Task DownloadBeatmapset_NoFolder_ReturnsNotFound()
    {
        var mapset = MakeMapset(200);
        _maps.SetBeatmaps = [MakeBeatmap(1, mapset)];

        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/200/download"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DownloadBeatmapset_FolderExists_ReturnsCorrectMimeType()
    {
        var mapset = MakeMapset(300);
        _maps.SetBeatmaps = [MakeBeatmap(1, mapset, "diff.osu")];
        var folder = MapsetFolder(300);
        await File.WriteAllTextAsync(Path.Combine(folder, "diff.osu"), "osu file format v14");

        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/300/download"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-osu-beatmap-archive", response.Content.Headers.ContentType?.MediaType);
    }

    // ---- GET /beatmapsets/{mapsetId}/storyboard ----

    [Fact]
    public async Task Storyboard_NoFolder_ReturnsNotFound()
    {
        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/400/storyboard"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Storyboard_FolderExistsNoOsb_ReturnsNotFound()
    {
        MapsetFolder(500);

        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/500/storyboard"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Storyboard_FolderHasOsbFile_ReturnsCorrectMimeType()
    {
        var folder = MapsetFolder(600);
        await File.WriteAllTextAsync(Path.Combine(folder, "storyboard.osb"), "[Events]");

        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/600/storyboard"));

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
        _maps.ByFilename = MakeBeatmap(1, MakeMapset(700), "Some Map.osu");
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

        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/scores/1/replay"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-osu-replay", response.Content.Headers.ContentType?.MediaType);
    }

    private sealed class StubMapRepository : IMapRepository
    {
        public Beatmap? OneBeatmap { get; set; }
        public Beatmap? ByFilename { get; set; }
        public IReadOnlyList<Beatmap> SetBeatmaps { get; set; } = [];

        public Task<Beatmap?> FetchOneAsync(int? id = null, string? md5 = null, string? filename = null,
            int? setId = null, bool includePrivate = false, CancellationToken cancellationToken = default)
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

        public Task<IReadOnlyList<Beatmap>> FetchAllBySetIdAsync(int setId, bool includePrivate = false,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SetBeatmaps.Count > 0 && SetBeatmaps[0].Mapset.Id == setId
                ? SetBeatmaps
                : (IReadOnlyList<Beatmap>)[]);
        }
    }

    private sealed class StubMapsetRepository : IMapsetRepository
    {
        public Mapset? Mapset { get; set; }

        public Task<Mapset?> FetchByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Mapset?.Id == id ? Mapset : null);
        }

        public Task<Mapset> UpsertAsync(Mapset mapset, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(mapset);
        }

        public Task DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<int> FetchMaxIdAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }

        public Task<IReadOnlyList<int>> FetchAllIdsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<int>>(Mapset is not null ? [Mapset.Id] : []);
        }

        public Task<IReadOnlyList<Mapset>> FetchPageAsync(int offset, int limit, bool onlyWithVisibleBeatmaps,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Mapset>>(Mapset is not null ? [Mapset] : []);
        }

        public Task SetFrozenAsync(int id, bool frozen, CancellationToken cancellationToken = default)
        {
            if (Mapset?.Id == id) Mapset = Mapset with { IsFrozen = frozen };
            return Task.CompletedTask;
        }

        public Task SetPrivateAsync(int id, bool isPrivate, CancellationToken cancellationToken = default)
        {
            if (Mapset?.Id == id) Mapset = Mapset with { IsPrivate = isPrivate };
            return Task.CompletedTask;
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

        public Task<ScoreRow?> FetchByIdAsync(long id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ScoreRow?>(null);
        }

        public Task<IReadOnlyList<ScoreRow>> FetchPageAsync(int offset, int limit,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ScoreRow>>([]);
        }

        public Task<IReadOnlyList<RoundScoreRow>> FetchByRoundIdAsync(int roundId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<RoundScoreRow>>([]);
        }

        public Task InvalidateByMapMd5Async(string mapMd5, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
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
