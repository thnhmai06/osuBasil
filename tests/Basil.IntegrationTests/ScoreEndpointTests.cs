using System.Net;
using System.Net.Http.Json;
using Basil.Application.Abstractions.Scores;
using Basil.Application.Configuration;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.IntegrationTests;

/// <summary>
///     Covers the new public `/scores` routes: `GET /scores/{scoreId}` (a score's full row, new — a score
///     used to only be visible embedded in a match report) and `GET /scores/{scoreId}/replay` (a direct
///     rename of the old bare `GET /replays/{scoreId}`).
/// </summary>
public class ScoreEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly StubScoreRepository _scores = new();
    private readonly StubReplayStorage _replayStorage = new();

    public ScoreEndpointTests(WebApplicationFactory<Program> factory)
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
                    ["Basil:Server:MenuOnclickUrl"] = "https://example.test"
                });
            });
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IOptions<DatabaseOptions>>(Options.Create(new DatabaseOptions { Path = "" }));
                services.AddSingleton<IScoreRepository>(_scores);
                services.AddSingleton<IReplayStorage>(_replayStorage);
            });
        });
    }

    private static HttpRequestMessage MakeRequest(HttpMethod method, string path)
    {
        return new HttpRequestMessage(method, path) { Headers = { Host = "api.test.local" } };
    }

    [Fact]
    public async Task GetScore_UnknownId_ReturnsNotFound()
    {
        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/scores/999"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetScore_KnownId_ReturnsFullRow()
    {
        _scores.Row = new ScoreRow(
            42, null, null, new string('a', 32), 900_000, 98.5, 500, Mods.Hidden,
            300, 10, 5, 0, 0, 0, "S", GameMode.Standard, DateTime.UtcNow, 120_000,
            ClientFlags.Clean, 7, false, "checksum", DateTime.UtcNow);

        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/scores/42"));
        var body = await response.Content.ReadFromJsonAsync<ScoreShape>();

        response.EnsureSuccessStatusCode();
        Assert.NotNull(body);
        Assert.Equal(42, body!.Id);
        Assert.Equal(900_000, body.Score);
        Assert.Equal(7, body.UserId);
    }

    [Fact]
    public async Task GetReplay_UnknownScore_ReturnsNotFound()
    {
        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/scores/999/replay"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetReplay_KnownScoreNoStoredReplay_ReturnsNotFound()
    {
        _scores.Owner = new ScoreOwnerRow(7, GameMode.Standard);

        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/scores/42/replay"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetReplay_Found_ReturnsReplayBytes()
    {
        _scores.Owner = new ScoreOwnerRow(7, GameMode.Standard);
        _replayStorage.Bytes = [1, 2, 3, 4];

        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/scores/42/replay"));
        var bytes = await response.Content.ReadAsByteArrayAsync();

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/x-osu-replay", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, bytes);
    }

    private sealed record ScoreShape(long Id, long Score, int UserId);

    private sealed class StubScoreRepository : IScoreRepository
    {
        public ScoreRow? Row { get; set; }
        public ScoreOwnerRow? Owner { get; set; }

        public Task<long> CreateAsync(ScoreInsertRow row, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<int> FetchCountAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Row is null ? 0 : 1);
        }

        public Task<bool> ExistsByOnlineChecksumAsync(string onlineChecksum,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<FirstPlaceScoreRow?> FetchFirstPlaceScoreAsync(string mapMd5, GameMode mode,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ScoreOwnerRow?> FetchOwnerAsync(long scoreId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Owner);
        }

        public Task<ScoreRow?> FetchByIdAsync(long id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Row);
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
            throw new NotSupportedException();
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
