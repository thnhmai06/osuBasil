using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Configuration;
using Basil.Domain.Beatmaps;
using Basil.Infrastructure.Beatmaps;
using Basil.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.IntegrationTests;

/// <summary>
///     Covers the admin-key-gated `/beatmapsets` write routes: `PUT`/`DELETE` (both filesystem-first
///     and asynchronous — 202, never a synchronous DB touch) and `PATCH` (the combined frozen/private
///     write-lock those two respect — frozen blocks `PUT`/`DELETE`, private hides the mapset and every
///     beatmap under it from non-admin reads). A stub `IMapsetRepository` stands in for the database
///     (this suite is about the route/filesystem behavior, not persistence), while the mapset's
///     storage folder is a real temp directory so `Directory.Move`/zip-extraction actually run.
/// </summary>
public class BeatmapsetManagementEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string AdminKey = "correct-key";
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _dataDir = Directory.CreateTempSubdirectory("basil-mapset-mgmt-tests-").FullName;
    private readonly StubMapsetRepository _mapsets = new();
    private readonly StubMapRepository _maps = new();

    public BeatmapsetManagementEndpointTests(WebApplicationFactory<Program> factory)
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
                    ["Basil:Server:AdminKey"] = AdminKey
                });
            });
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IOptions<DatabaseOptions>>(Options.Create(new DatabaseOptions { Path = "" }));
                services.AddSingleton<IMapsetRepository>(_mapsets);
                services.AddSingleton<IMapRepository>(_maps);
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

    private static HttpRequestMessage MakeRequest(HttpMethod method, string path, string? adminKey = AdminKey)
    {
        var request = new HttpRequestMessage(method, path) { Headers = { Host = "api.test.local" } };
        if (adminKey is not null) request.Headers.Add("X-Admin-Key", adminKey);
        return request;
    }

    private string MapsetFolder(int setId)
    {
        var folder = Path.Combine(_dataDir, "Mapsets", $"{setId} Artist - Title");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static async Task<byte[]> MakeMinimalOszAsync()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            var entry = archive.CreateEntry("replacement.osu");
            await using var entryStream = entry.Open();
            await entryStream.WriteAsync("osu file format v14"u8.ToArray());
        }

        return stream.ToArray();
    }

    // ---- PUT /beatmapsets/{beatmapsetId} ----

    [Fact]
    public async Task PutBeatmapset_UnknownId_ReturnsNotFound()
    {
        var request = MakeRequest(HttpMethod.Put, "/beatmapsets/999999");
        request.Content = new MultipartFormDataContent { { new ByteArrayContent(await MakeMinimalOszAsync()), "file", "set.osz" } };

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PutBeatmapset_Frozen_ReturnsConflict()
    {
        _mapsets.Mapset = new Mapset(700, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow, IsFrozen: true);
        MapsetFolder(700);

        var request = MakeRequest(HttpMethod.Put, "/beatmapsets/700");
        request.Content = new MultipartFormDataContent { { new ByteArrayContent(await MakeMinimalOszAsync()), "file", "set.osz" } };

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PutBeatmapset_Valid_ExtractsIntoResolvedFolderAndReturns202()
    {
        _mapsets.Mapset = new Mapset(701, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow);
        var folder = MapsetFolder(701);
        await File.WriteAllTextAsync(Path.Combine(folder, "old.osu"), "stale content");

        var request = MakeRequest(HttpMethod.Put, "/beatmapsets/701");
        request.Content = new MultipartFormDataContent { { new ByteArrayContent(await MakeMinimalOszAsync()), "file", "set.osz" } };

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.True(File.Exists(Path.Combine(folder, "replacement.osu")));
        Assert.True(File.Exists(Path.Combine(folder, "old.osu")));
    }

    // ---- DELETE /beatmapsets/{beatmapsetId} ----

    [Fact]
    public async Task DeleteBeatmapset_UnknownId_ReturnsNotFound()
    {
        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Delete, "/beatmapsets/999999"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteBeatmapset_Frozen_ReturnsConflict_FolderUntouched()
    {
        _mapsets.Mapset = new Mapset(800, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow, IsFrozen: true);
        var folder = MapsetFolder(800);

        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Delete, "/beatmapsets/800"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public async Task DeleteBeatmapset_Valid_RenamesToDeletedMarkerAndReturns202()
    {
        _mapsets.Mapset = new Mapset(801, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow);
        var folder = MapsetFolder(801);

        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Delete, "/beatmapsets/801"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.False(Directory.Exists(folder));
        var renamed = Directory.EnumerateDirectories(Path.Combine(_dataDir, "Mapsets"))
            .FirstOrDefault(d => d.Contains(BeatmapIngestionService.DeletedFolderInfix));
        Assert.NotNull(renamed);
    }

    // ---- PATCH /beatmapsets/{beatmapsetId} ----

    [Fact]
    public async Task PatchBeatmapset_UnknownId_ReturnsNotFound()
    {
        var request = MakeRequest(HttpMethod.Patch, "/beatmapsets/999999");
        request.Content = JsonContent.Create(new { frozen = true });

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PatchBeatmapset_TogglesFrozenAndPrivateTogether()
    {
        _mapsets.Mapset = new Mapset(900, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow);

        var setRequest = MakeRequest(HttpMethod.Patch, "/beatmapsets/900");
        setRequest.Content = JsonContent.Create(new { frozen = true, @private = true });
        var setResponse = await _factory.CreateClient().SendAsync(setRequest);

        Assert.True(setResponse.IsSuccessStatusCode);
        Assert.True(_mapsets.Mapset!.IsFrozen);
        Assert.True(_mapsets.Mapset!.IsPrivate);

        var clearRequest = MakeRequest(HttpMethod.Patch, "/beatmapsets/900");
        clearRequest.Content = JsonContent.Create(new { frozen = false, @private = false });
        await _factory.CreateClient().SendAsync(clearRequest);

        Assert.False(_mapsets.Mapset!.IsFrozen);
        Assert.False(_mapsets.Mapset!.IsPrivate);
    }

    [Fact]
    public async Task PatchBeatmapset_OmittedField_LeavesItUnchanged()
    {
        _mapsets.Mapset = new Mapset(902, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow,
            IsPrivate: true);

        var request = MakeRequest(HttpMethod.Patch, "/beatmapsets/902");
        request.Content = JsonContent.Create(new { frozen = true });
        await _factory.CreateClient().SendAsync(request);

        Assert.True(_mapsets.Mapset!.IsFrozen);
        Assert.True(_mapsets.Mapset!.IsPrivate);
    }

    [Fact]
    public async Task PatchBeatmapset_MissingAdminKey_ReturnsUnauthorized()
    {
        _mapsets.Mapset = new Mapset(901, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow);

        var request = MakeRequest(HttpMethod.Patch, "/beatmapsets/901", adminKey: null);
        request.Content = JsonContent.Create(new { frozen = true });

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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

    private sealed class StubMapRepository : IMapRepository
    {
        public Task<Beatmap?> FetchOneAsync(int? id = null, string? md5 = null, string? filename = null,
            int? setId = null, bool includePrivate = false, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Beatmap?>(null);
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
            return Task.FromResult<IReadOnlyList<Beatmap>>([]);
        }
    }
}
