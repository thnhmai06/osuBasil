using System.Net.Http.Headers;
using System.Text.Json;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Configuration;
using Basil.Application.Sessions.Multiplayer;
using Basil.Application.Sessions.Spectating;
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
///     Covers the live SSE layer end to end over TestServer's in-memory HTTP transport — no real
///     osu! client or tourney manager involved, but a real streamed GET request, a real
///     IMatchLiveEvents/IPlayerInputEvents publish, and a real incremental read of the response
///     body. Publishes are retried in a short poll loop rather than fired once: a client's request
///     completing does not guarantee the server-side handler has reached the event subscription yet
///     (both run in-process with no real network latency between them, so this race is easy to lose
///     without the retry).
/// </summary>
public class LiveSseEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public LiveSseEndpointTests(WebApplicationFactory<Program> factory)
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
                // The "main" SSE route now checks whether a match has actually closed (persisted with
                // EndedAt set) before opening a stream — a stub avoids needing a real SQLite file
                // just to answer "no, nothing here has ever been persisted" for these plumbing tests.
                services.AddSingleton<IMatchPersistenceRepository>(new NeverPersistedMatchRepository());
            });
        });
    }

    [Fact]
    public async Task MainChannel_ReceivesWhateverIsPublishedForThatMatchId()
    {
        var events = _factory.Services.GetRequiredService<IMatchLiveEvents>();

        var (eventType, data) = await ReceiveAfterPublishAsync("/match/5",
            () => events.PublishMain(5, JsonSerializer.SerializeToUtf8Bytes(new { hello = "world" })));

        Assert.Equal("main", eventType);
        Assert.Contains("world", data);
    }

    [Fact]
    public async Task SpecChannel_ReceivesWhateverIsPublishedForThatPlayerId()
    {
        var events = _factory.Services.GetRequiredService<IPlayerInputEvents>();

        var (eventType, data) = await ReceiveAfterPublishAsync("/user/7/live",
            () => events.PublishInput(7, "frame-data"u8.ToArray()));

        Assert.Equal("input", eventType);
        Assert.Equal("frame-data", data);
    }

    [Fact]
    public async Task SpecChannel_IgnoresPublishesForOtherPlayerIds()
    {
        var events = _factory.Services.GetRequiredService<IPlayerInputEvents>();

        var (_, data) = await ReceiveAfterPublishAsync("/user/7/live", () =>
        {
            events.PublishInput(8, "not for player 7"u8.ToArray());
            events.PublishInput(7, "for player 7"u8.ToArray());
        });

        Assert.Equal("for player 7", data);
    }

    [Fact]
    public async Task PlayerChannel_OnlyReceivesPublishesForThatPlayerName()
    {
        var events = _factory.Services.GetRequiredService<IMatchLiveEvents>();

        var (eventType, data) = await ReceiveAfterPublishAsync("/match/9/alice", () =>
        {
            events.PublishPlayer(9, "bob", "not for alice"u8.ToArray());
            events.PublishPlayer(9, "alice", "for alice"u8.ToArray());
        });

        Assert.Equal("playerScore", eventType);
        Assert.Equal("for alice", data);
    }

    [Fact]
    public async Task MainChannel_OnlyReceivesPublishesForItsOwnMatchId_NotOtherMatches()
    {
        var events = _factory.Services.GetRequiredService<IMatchLiveEvents>();

        var (_, data) = await ReceiveAfterPublishAsync("/match/11", () =>
        {
            events.PublishMain(12, "wrong match"u8.ToArray());
            events.PublishMain(11, "right match"u8.ToArray());
        });

        Assert.Equal("right match", data);
    }

    /// <summary>
    ///     Connects a streamed GET to <paramref name="path" /> with an EventSource-style Accept
    ///     header, calls <paramref name="publish" /> repeatedly (every 50ms) until the next SSE
    ///     message arrives or 10s elapse, and returns that message's `event:`/`data:` lines.
    ///     Publishing has to keep running across BOTH the initial connect and the first read — an
    ///     SSE response apparently doesn't flush its headers until its first write, so awaiting
    ///     SendAsync before ever publishing anything would deadlock (nothing would ever trigger that
    ///     first write).
    /// </summary>
    private async Task<(string? EventType, string Data)> ReceiveAfterPublishAsync(string path, Action publish)
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, path) { Headers = { Host = "api.test.local" } };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var pipelineTask = ConnectAndReadOneEventAsync(client, request, cts.Token);

        while (!pipelineTask.IsCompleted)
        {
            publish();
            await Task.WhenAny(pipelineTask, Task.Delay(50, CancellationToken.None));
        }

        return await pipelineTask;
    }

    private static async Task<(string? EventType, string Data)> ConnectAndReadOneEventAsync(
        HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response =
            await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        return await ReadNextEventAsync(reader, cancellationToken);
    }

    private static async Task<(string? EventType, string Data)> ReadNextEventAsync(StreamReader reader,
        CancellationToken cancellationToken)
    {
        string? eventType = null;
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) throw new IOException("Stream ended unexpectedly.");

            if (line.StartsWith("event: ", StringComparison.Ordinal))
                eventType = line["event: ".Length..];
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
                return (eventType, line["data: ".Length..]);
        }
    }

    /// <summary>Every id is reported as never-persisted — exactly what these tests need, without a real SQLite file.</summary>
    private sealed class NeverPersistedMatchRepository : IMatchPersistenceRepository
    {
        public Task<int> CreateMatchAsync(string name, DateTime createdAt, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetMatchEndedAsync(int matchId, DateTime endedAt, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> CreateRoundAsync(int matchId, int roundIndex, int beatmapId, string mapMd5,
            GameMode mode, MatchWinCondition winCondition, MatchTeamType teamType,
            string beatmapArtist, string beatmapTitle, string beatmapVersion, string beatmapCreator,
            Mods mods, DateTime startedAt, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetRoundEndedAsync(int roundId, DateTime endedAt, bool aborted, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MatchRow?> FetchMatchAsync(int matchId, CancellationToken cancellationToken = default) =>
            Task.FromResult<MatchRow?>(null);

        public Task<IReadOnlyList<RoundRow>> FetchRoundsAsync(int matchId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RoundRow>>([]);

        public Task<IReadOnlyList<MatchRow>> FetchAllMatchesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MatchRow>>([]);

        public Task DeleteMatchAsync(int matchId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CreateEventAsync(MatchEventRow row, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<MatchEventRow>> FetchEventsAsync(int matchId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MatchEventRow>>([]);

        public Task<IReadOnlyList<MatchRow>> FetchUnrecoveredMatchesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MatchRow>>([]);

        public Task<IReadOnlyList<RoundRow>> FetchUnrecoveredRoundsAsync(int matchId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RoundRow>>([]);
    }
}
