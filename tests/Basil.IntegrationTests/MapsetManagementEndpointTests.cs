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
///     Covers the admin-key-gated `/mapset` write routes: `PUT`/`DELETE` (both filesystem-first and
///     asynchronous — 202, never a synchronous DB touch) and `PATCH .../freeze` (the write-lock those
///     two respect). A stub `IMapsetRepository` stands in for the database (this suite is about the
///     route/filesystem behavior, not persistence), while the mapset's storage folder is a real temp
///     directory so `Directory.Move`/zip-extraction actually run.
/// </summary>
public class MapsetManagementEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string AdminKey = "correct-key";
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _dataDir = Directory.CreateTempSubdirectory("basil-mapset-mgmt-tests-").FullName;
    private readonly StubMapsetRepository _mapsets = new();

    public MapsetManagementEndpointTests(WebApplicationFactory<Program> factory)
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

    // ---- PUT /mapset/{id} ----

    [Fact]
    public async Task PutMapset_UnknownId_ReturnsNotFound()
    {
        var request = MakeRequest(HttpMethod.Put, "/mapset/999999");
        request.Content = new MultipartFormDataContent { { new ByteArrayContent(await MakeMinimalOszAsync()), "file", "set.osz" } };

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PutMapset_Frozen_ReturnsConflict()
    {
        _mapsets.Mapset = new Mapset(700, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow, IsFrozen: true);
        MapsetFolder(700);

        var request = MakeRequest(HttpMethod.Put, "/mapset/700");
        request.Content = new MultipartFormDataContent { { new ByteArrayContent(await MakeMinimalOszAsync()), "file", "set.osz" } };

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PutMapset_Valid_ExtractsIntoResolvedFolderAndReturns202()
    {
        _mapsets.Mapset = new Mapset(701, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow);
        var folder = MapsetFolder(701);
        await File.WriteAllTextAsync(Path.Combine(folder, "old.osu"), "stale content");

        var request = MakeRequest(HttpMethod.Put, "/mapset/701");
        request.Content = new MultipartFormDataContent { { new ByteArrayContent(await MakeMinimalOszAsync()), "file", "set.osz" } };

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.True(File.Exists(Path.Combine(folder, "replacement.osu")));
        Assert.True(File.Exists(Path.Combine(folder, "old.osu")));
    }

    // ---- DELETE /mapset/{id} ----

    [Fact]
    public async Task DeleteMapset_UnknownId_ReturnsNotFound()
    {
        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Delete, "/mapset/999999"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteMapset_Frozen_ReturnsConflict_FolderUntouched()
    {
        _mapsets.Mapset = new Mapset(800, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow, IsFrozen: true);
        var folder = MapsetFolder(800);

        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Delete, "/mapset/800"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public async Task DeleteMapset_Valid_RenamesToDeletedMarkerAndReturns202()
    {
        _mapsets.Mapset = new Mapset(801, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow);
        var folder = MapsetFolder(801);

        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Delete, "/mapset/801"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.False(Directory.Exists(folder));
        var renamed = Directory.EnumerateDirectories(Path.Combine(_dataDir, "Mapsets"))
            .FirstOrDefault(d => d.Contains(BeatmapIngestionService.DeletedFolderInfix));
        Assert.NotNull(renamed);
    }

    // ---- PATCH /mapset/{id}/freeze ----

    [Fact]
    public async Task PatchFreeze_UnknownId_ReturnsNotFound()
    {
        var request = MakeRequest(HttpMethod.Patch, "/mapset/999999/freeze");
        request.Content = JsonContent.Create(new { frozen = true });

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PatchFreeze_TogglesFrozenState()
    {
        _mapsets.Mapset = new Mapset(900, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow);

        var freezeRequest = MakeRequest(HttpMethod.Patch, "/mapset/900/freeze");
        freezeRequest.Content = JsonContent.Create(new { frozen = true });
        var freezeResponse = await _factory.CreateClient().SendAsync(freezeRequest);

        Assert.True(freezeResponse.IsSuccessStatusCode);
        Assert.True(_mapsets.Mapset!.IsFrozen);

        var unfreezeRequest = MakeRequest(HttpMethod.Patch, "/mapset/900/freeze");
        unfreezeRequest.Content = JsonContent.Create(new { frozen = false });
        await _factory.CreateClient().SendAsync(unfreezeRequest);

        Assert.False(_mapsets.Mapset!.IsFrozen);
    }

    [Fact]
    public async Task PatchFreeze_MissingAdminKey_ReturnsUnauthorized()
    {
        _mapsets.Mapset = new Mapset(901, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow);

        var request = MakeRequest(HttpMethod.Patch, "/mapset/901/freeze", adminKey: null);
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
}
