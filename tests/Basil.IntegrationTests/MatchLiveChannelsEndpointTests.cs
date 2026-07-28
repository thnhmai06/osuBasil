using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configuration;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Login;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.IntegrationTests;

/// <summary>Covers the two newest SSE channels: GET /matches/{matchId}/live and GET /matches/{matchId}/live/{slotIndex}.</summary>
public class MatchLiveChannelsEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AdminKey = "correct-key";
    private readonly WebApplicationFactory<Program> _factory;

    public MatchLiveChannelsEndpointTests(WebApplicationFactory<Program> factory)
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
                services.AddSingleton<IMatchPersistenceRepository>(new NoopMatchPersistenceRepository());
                services.AddSingleton<IUserRepository>(new NoopUserRepository());
            });
        });
    }

    [Fact]
    public async Task LiveChannel_ReceivesWhateverIsPublishedForThatMatchId()
    {
        var matchId = await CreateMatchAsync();
        var events = _factory.Services.GetRequiredService<IMatchLiveEvents>();

        // discardFirst: true — POST /matches warms this match's main SnapshotChannel immediately
        // (same reasoning as LiveSlotChannel_ReceivesSlotEventsForItsOwnSlotOnly below), so the first
        // event off a fresh connect is that warm full snapshot (inProgress: false), not this test's
        // manually published delta.
        var (eventType, data) = await ReceiveAfterPublishAsync($"/matches/{matchId}/live",
            () => events.PublishMain(matchId, JsonSerializer.SerializeToUtf8Bytes(new { inProgress = true })),
            true);

        Assert.Equal("main", eventType);
        Assert.Contains("true", data);
    }

    [Fact]
    public async Task LiveChannel_UnknownMatch_ReturnsConflictEnvelope()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/matches/999999/live")
            { Headers = { Host = "api.test.local" } };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("success").GetBoolean());
    }

    private async Task<int> CreateMatchAsync()
    {
        var client = _factory.CreateClient();
        var createRequest = new HttpRequestMessage(HttpMethod.Post, "/matches")
            { Headers = { Host = "api.test.local" } };
        createRequest.Headers.Add("X-Admin-Key", AdminKey);
        createRequest.Content = JsonContent.Create(new { });
        var createResponse = await client.SendAsync(createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        return created.GetProperty("data").GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task LiveSlotChannel_UnknownMatch_ReturnsNotFound()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/matches/999999/live/1")
            { Headers = { Host = "api.test.local" } };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task LiveSlotChannel_ReceivesSlotEventsForItsOwnSlotOnly()
    {
        var client = _factory.CreateClient();
        var createRequest = new HttpRequestMessage(HttpMethod.Post, "/matches")
            { Headers = { Host = "api.test.local" } };
        createRequest.Headers.Add("X-Admin-Key", AdminKey);
        createRequest.Content = JsonContent.Create(new { });
        var createResponse = await client.SendAsync(createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var matchId = created.GetProperty("data").GetProperty("id").GetInt32();

        var events = _factory.Services.GetRequiredService<IMatchLiveEvents>();

        // discardFirst: true — POST /matches now applies every CreateMatchRequest field unconditionally
        // (SetPrivate/SetSize/... all call EnqueueState), so this slot's SnapshotChannel is already warm
        // by the time the match is created; the first event off a fresh connect is that warm snapshot,
        // not a published delta.
        var (eventType, data) = await ReceiveAfterPublishAsync($"/matches/{matchId}/live/1", () =>
        {
            events.PublishSlot(matchId, 5, "wrong slot"u8.ToArray());
            events.PublishSlot(matchId, 0, "right slot"u8.ToArray());
        }, true);

        Assert.Equal("slot", eventType);
        Assert.Equal("right slot", data);
    }

    private async Task<(string? EventType, string Data)> ReceiveAfterPublishAsync(string path, Action publish,
        bool discardFirst = false)
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, path) { Headers = { Host = "api.test.local" } };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var pipelineTask = ConnectAndReadOneEventAsync(client, request, discardFirst, cts.Token);

        while (!pipelineTask.IsCompleted)
        {
            publish();
            await Task.WhenAny(pipelineTask, Task.Delay(50, CancellationToken.None));
        }

        return await pipelineTask;
    }

    private static async Task<(string? EventType, string Data)> ConnectAndReadOneEventAsync(
        HttpClient client, HttpRequestMessage request, bool discardFirst, CancellationToken cancellationToken)
    {
        using var response =
            await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        if (discardFirst) await ReadNextEventAsync(reader, cancellationToken);
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

    private sealed class NoopMatchPersistenceRepository : IMatchPersistenceRepository
    {
        private int _nextId = 1;

        public Task<int> CreateMatchAsync(string name, DateTime createdAt,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_nextId++);
        }

        public Task SetMatchEndedAsync(int matchId, DateTime endedAt, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<int> CreateRoundAsync(int matchId, int roundIndex, string mapMd5,
            GameMode mode, MatchWinCondition winCondition, MatchTeamType teamType,
            Mods mods, DateTime startedAt, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task SetRoundEndedAsync(int roundId, DateTime endedAt, bool aborted,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<MatchRow?> FetchMatchAsync(int matchId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<MatchRow?>(null);
        }

        public Task<IReadOnlyList<RoundRow>> FetchRoundsAsync(int matchId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<RoundRow>>([]);
        }

        public Task<IReadOnlyList<MatchRow>> FetchAllMatchesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<MatchRow>>([]);
        }

        public Task DeleteMatchAsync(int matchId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task CreateEventAsync(MatchEventRow row, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MatchEventRow>> FetchEventsAsync(int matchId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<MatchEventRow>>([]);
        }

        public Task<IReadOnlyList<MatchRow>> FetchUnrecoveredMatchesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<MatchRow>>([]);
        }

        public Task<IReadOnlyList<RoundRow>> FetchUnrecoveredRoundsAsync(int matchId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<RoundRow>>([]);
        }
    }

    /// <summary>
    ///     Stands in for the real DB-backed <see cref="IUserRepository" /> so an offline/unregistered id
    ///     referenced by these tests resolves to "no account" — UserBriefResolver's documented fallback —
    ///     instead of hitting the real SQLite path these tests otherwise never need a working database
    ///     connection for.
    /// </summary>
    private sealed class NoopUserRepository : IUserRepository
    {
        public Task<User?> FetchByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<User?>(null);
        }

        public Task<User?> FetchByNameAsync(string name, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<User?>(null);
        }

        public Task<string?> FetchPasswordHashAsync(int id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }

        public Task UpdateCountryAsync(int id, Country country, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task UpdatePrivilegesAsync(int id, UserPrivileges privilege,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task UpdateNameAsync(int id, string name, string safeName, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<User?> CreateAsync(string name, string pwBcrypt, Country country, UserPrivileges? privilege = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<User?>(null);
        }

        public Task<IReadOnlyList<User>> FetchAllAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<User>>([]);
        }
    }
}