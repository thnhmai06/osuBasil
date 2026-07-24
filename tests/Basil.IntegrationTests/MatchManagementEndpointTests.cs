using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Basil.Application.Abstractions.Multiplayer;
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
///     Covers the new `/matches` list/create/settings/action routes end to end — in particular, this is
///     the first real endpoint <see cref="Basil.Web.Auth.AdminKeyAuthenticationHandler" />'s
///     `RequireAuthorization` policy is actually attached to, so the missing/wrong-key -&gt; 401 path
///     is verified through the full middleware pipeline here, not just the handler in isolation.
/// </summary>
public class MatchManagementEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AdminKey = "correct-key";
    private readonly WebApplicationFactory<Program> _factory;
    private readonly FakeMatchPersistenceRepository _matchPersistence = new();

    public MatchManagementEndpointTests(WebApplicationFactory<Program> factory)
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
                services.AddSingleton<IMatchPersistenceRepository>(_matchPersistence);
            });
        });
    }

    private static HttpRequestMessage MakeRequest(HttpMethod method, string path, string? adminKey = null)
    {
        var request = new HttpRequestMessage(method, path) { Headers = { Host = "api.test.local" } };
        if (adminKey is not null) request.Headers.Add("X-Admin-Key", adminKey);
        return request;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task PostMatch_MissingOrWrongAdminKey_ReturnsUnauthorized(string? adminKey)
    {
        var client = _factory.CreateClient();
        var request = MakeRequest(HttpMethod.Post, "/matches", adminKey);
        request.Content = JsonContent.Create(new { name = "Test" });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostMatch_ValidAdminKey_CreatesEmptyMatchWithHostZero()
    {
        var client = _factory.CreateClient();
        var request = MakeRequest(HttpMethod.Post, "/matches", AdminKey);
        request.Content = JsonContent.Create(new { name = "Grand Finals" });

        var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Grand Finals", json.GetProperty("name").GetString());
        Assert.False(json.GetProperty("hasPassword").GetBoolean());
        Assert.False(json.GetProperty("isPrivate").GetBoolean());
        Assert.True(json.GetProperty("hostId").ValueKind is JsonValueKind.Null);
    }

    [Fact]
    public async Task GetMatch_ListsCreatedMatchByDefault_OnlineStatus()
    {
        var client = _factory.CreateClient();
        var createRequest = MakeRequest(HttpMethod.Post, "/matches", AdminKey);
        createRequest.Content = JsonContent.Create(new { name = "Listed Match" });
        var createResponse = await client.SendAsync(createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetInt32();

        var response = await client.SendAsync(MakeRequest(HttpMethod.Get, "/matches"));

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(items, item => item.GetProperty("id").GetInt32() == id && item.GetProperty("isOpen").GetBoolean());
    }

    [Fact]
    public async Task PatchSettings_UpdatesNameAndSize()
    {
        var client = _factory.CreateClient();
        var createRequest = MakeRequest(HttpMethod.Post, "/matches", AdminKey);
        createRequest.Content = JsonContent.Create(new { });
        var createResponse = await client.SendAsync(createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetInt32();

        var patchRequest = MakeRequest(HttpMethod.Patch, $"/matches/{id}/settings", AdminKey);
        patchRequest.Content = JsonContent.Create(new { name = "Renamed", size = 4 });
        var patchResponse = await client.SendAsync(patchRequest);

        patchResponse.EnsureSuccessStatusCode();
        var json = await patchResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Renamed", json.GetProperty("name").GetString());
        Assert.Equal(4, json.GetProperty("size").GetInt32());
    }

    [Fact]
    public async Task PatchSettings_UnknownMatchId_ReturnsNotFound()
    {
        var client = _factory.CreateClient();
        var request = MakeRequest(HttpMethod.Patch, "/matches/999999/settings", AdminKey);
        request.Content = JsonContent.Create(new { name = "Nope" });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostAction_Close_RemovesMatchFromOnlineListing()
    {
        var client = _factory.CreateClient();
        var createRequest = MakeRequest(HttpMethod.Post, "/matches", AdminKey);
        createRequest.Content = JsonContent.Create(new { });
        var createResponse = await client.SendAsync(createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetInt32();

        var closeRequest = MakeRequest(HttpMethod.Post, $"/matches/{id}/close", AdminKey);
        closeRequest.Content = JsonContent.Create(new { });
        var closeResponse = await client.SendAsync(closeRequest);
        closeResponse.EnsureSuccessStatusCode();

        var listResponse = await client.SendAsync(MakeRequest(HttpMethod.Get, "/matches"));
        var json = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items").EnumerateArray().ToList();
        Assert.DoesNotContain(items, item => item.GetProperty("id").GetInt32() == id);
    }

    /// <summary>
    ///     End-to-end smoke test per the route-redesign plan's Verification section: create a match,
    ///     open its settings SSE channel, confirm the first event is a full snapshot, confirm a
    ///     subsequent settings write produces a delta-only event (not a re-sent full snapshot), and
    ///     confirm the raw password is never present in either payload — only `hasPassword`.
    /// </summary>
    [Fact]
    public async Task Smoke_CreateMatch_OpenSettingsSse_FirstEventFull_ThenPatchProducesDeltaWithoutPassword()
    {
        var client = _factory.CreateClient();
        var createRequest = MakeRequest(HttpMethod.Post, "/matches", AdminKey);
        createRequest.Content = JsonContent.Create(new { name = "Smoke Test", password = "hunter2" });
        var createResponse = await client.SendAsync(createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetInt32();

        var streamClient = _factory.CreateClient();
        var streamRequest = new HttpRequestMessage(HttpMethod.Get, $"/matches/{id}/settings")
            { Headers = { Host = "api.test.local" } };
        streamRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var response =
            await streamClient.SendAsync(streamRequest, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);

        var (firstEvent, firstData) = await ReadNextSseEventAsync(reader, cts.Token);
        Assert.Equal("settings", firstEvent);
        Assert.Contains("\"name\":\"Smoke Test\"", firstData);
        Assert.DoesNotContain("hunter2", firstData);

        var patchRequest = MakeRequest(HttpMethod.Patch, $"/matches/{id}/settings", AdminKey);
        patchRequest.Content = JsonContent.Create(new { name = "Renamed Smoke Test" });
        var patchResponseTask = client.SendAsync(patchRequest, cts.Token);

        var (secondEvent, secondData) = await ReadNextSseEventAsync(reader, cts.Token);
        await patchResponseTask;

        Assert.Equal("settings", secondEvent);
        Assert.Contains("\"name\":\"Renamed Smoke Test\"", secondData);
        // A delta only carries the changed field(s) -- unrelated settings fields from the first
        // event must not be repeated.
        Assert.DoesNotContain("mapId", secondData);
        Assert.DoesNotContain("winCondition", secondData);
        Assert.DoesNotContain("hunter2", secondData);
    }

    [Fact]
    public async Task Smoke_GetUserLive_UserIdZero_IsBlocked()
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(MakeRequest(HttpMethod.Get, "/users/0/live"));

        // BasilBot (user id 0) has no gameplay stream of its own -- blocked rather than opening a
        // stream that would never receive a frame. Implemented as 400 (not the plan's literal "404"
        // wording) since the id is malformed input for this route, not a missing resource -- same
        // reasoning already applied consistently to every other BasilBot-id guard in UserRoutes.cs.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<(string? EventType, string Data)> ReadNextSseEventAsync(StreamReader reader,
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

    /// <summary>Minimal in-memory fake so CreateEmptyAsync/FetchAllMatchesAsync/DeleteMatchAsync behave realistically without a real SQLite file.</summary>
    private sealed class FakeMatchPersistenceRepository : IMatchPersistenceRepository
    {
        private readonly Dictionary<int, MatchRow> _matches = [];
        private int _nextId = 1;

        public Task<int> CreateMatchAsync(string name, DateTime createdAt, CancellationToken cancellationToken = default)
        {
            var id = _nextId++;
            _matches[id] = new MatchRow(id, name, createdAt, null);
            return Task.FromResult(id);
        }

        public Task SetMatchEndedAsync(int matchId, DateTime endedAt, CancellationToken cancellationToken = default)
        {
            if (_matches.TryGetValue(matchId, out var row))
                _matches[matchId] = row with { EndedAt = endedAt };
            return Task.CompletedTask;
        }

        public Task<int> CreateRoundAsync(int matchId, int roundIndex, string mapMd5,
            GameMode mode, MatchWinCondition winCondition, MatchTeamType teamType,
            Mods mods, DateTime startedAt, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetRoundEndedAsync(int roundId, DateTime endedAt, bool aborted, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MatchRow?> FetchMatchAsync(int matchId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_matches.GetValueOrDefault(matchId));

        public Task<IReadOnlyList<RoundRow>> FetchRoundsAsync(int matchId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RoundRow>>([]);

        public Task<IReadOnlyList<MatchRow>> FetchAllMatchesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MatchRow>>(_matches.Values.OrderByDescending(m => m.Id).ToList());

        public Task DeleteMatchAsync(int matchId, CancellationToken cancellationToken = default)
        {
            _matches.Remove(matchId);
            return Task.CompletedTask;
        }

        public Task CreateEventAsync(MatchEventRow row, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<MatchEventRow>> FetchEventsAsync(int matchId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MatchEventRow>>([]);

        public Task<IReadOnlyList<MatchRow>> FetchUnrecoveredMatchesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MatchRow>>([]);

        public Task<IReadOnlyList<RoundRow>> FetchUnrecoveredRoundsAsync(int matchId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RoundRow>>([]);
    }
}
