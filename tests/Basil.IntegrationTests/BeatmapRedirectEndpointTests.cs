using System.Net;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Domain.Beatmaps;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.IntegrationTests;

/// <summary>
///     Ported from app/api/domains/osu.py's get_osz/get_updated_beatmap. This server has no local
///     .osz/.osu file storage and no internet default — see MirrorOptions' doc comment. Both
///     endpoints report unavailability rather than reaching out to the internet by default;
///     /d/{set_id} only redirects if an operator explicitly configures MirrorOptions:DownloadEndpoint.
/// </summary>
public class BeatmapRedirectEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static WebApplicationFactory<Program> Configure(WebApplicationFactory<Program> factory,
        string? downloadEndpoint = null)
    {
        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var settings = new Dictionary<string, string?>
                {
                    ["ServerBehavior:Domain"] = "test.local",
                    ["ServerBehavior:CommandPrefix"] = "!",
                    ["ServerBehavior:MenuIconUrl"] = "https://example.test/icon.png",
                    ["ServerBehavior:MenuOnclickUrl"] = "https://example.test",
                    ["Database:Path"] = ""
                };
                if (downloadEndpoint is not null) settings["Mirror:DownloadEndpoint"] = downloadEndpoint;

                config.AddInMemoryCollection(settings);
            });
            builder.ConfigureServices(services =>
                services.AddSingleton<IMapRepository, NullMapRepository>());
        });
    }

    // Database:Path is "" for this test host (no real DB) — /d/{setId} and /web/maps/{filename}
    // still call IMapRepository unconditionally, so they need a stub rather than a real connection.
    private sealed class NullMapRepository : IMapRepository
    {
        public Task<Beatmap?> FetchOneAsync(int? id = null, string? md5 = null, string? filename = null,
            int? setId = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Beatmap?>(null);
        }

        public Task UpsertAsync(Beatmap beatmap, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DeleteByMd5Async(string md5, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<IReadOnlyList<Beatmap>>> SearchAsync(string? query, GameMode? mode,
            RankedStatus? status, int offset, int amount, CancellationToken cancellationToken = default)
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

        public Task<IReadOnlyList<Beatmap>> FetchAllBySetIdAsync(int setId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Beatmap>>([]);
        }
    }

    private static HttpRequestMessage MakeRequest(string path)
    {
        return new HttpRequestMessage(HttpMethod.Get, path) { Headers = { Host = "osu.test.local" } };
    }

    [Fact]
    public async Task Download_NoDownloadEndpointConfigured_ReturnsUnavailableMessage_NoRedirect()
    {
        var client = Configure(factory)
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.SendAsync(MakeRequest("/d/12345"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("not available", body);
    }

    [Fact]
    public async Task Download_EndpointConfigured_RedirectsWithVideoFlag()
    {
        var client = Configure(factory, "https://mirror.local/d")
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.SendAsync(MakeRequest("/d/12345"));

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("https://mirror.local/d/12345?n=1", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Download_NoVideoFlagSuffix_StripsNAndSetsNZero()
    {
        var client = Configure(factory, "https://mirror.local/d")
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.SendAsync(MakeRequest("/d/12345n"));

        Assert.Equal("https://mirror.local/d/12345?n=0", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task MapFile_ReturnsNotFound_WhenFilenameUnknown()
    {
        var client = Configure(factory)
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.SendAsync(MakeRequest("/web/maps/Some%20Map.osu"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}