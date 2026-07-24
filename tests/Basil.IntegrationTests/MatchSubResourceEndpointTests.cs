using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configuration;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.IntegrationTests;

/// <summary>
///     Covers the `/matches/{matchId}/{hosts,refs,ban,kick,invite,slots,timer,abort,close}` routes
///     that replaced the old generic `POST /matches/{matchId}/{action}` dispatch. Matches are created
///     through the real `POST /matches` route (no chat "sender", host id 0), then seated with real
///     <see cref="PlayerSession" />s registered directly against the app's actual DI-resolved
///     <see cref="IPlayerSessionRegistry" />/<see cref="MatchMembershipService" /> — the same
///     production singletons the routes themselves use.
/// </summary>
public class MatchSubResourceEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AdminKey = "correct-key";
    private readonly WebApplicationFactory<Program> _factory;

    public MatchSubResourceEndpointTests(WebApplicationFactory<Program> factory)
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

    private static HttpRequestMessage MakeRequest(HttpMethod method, string path, string? adminKey = AdminKey)
    {
        var request = new HttpRequestMessage(method, path) { Headers = { Host = "api.test.local" } };
        if (adminKey is not null) request.Headers.Add("X-Admin-Key", adminKey);
        return request;
    }

    private async Task<int> CreateMatchAsync(HttpClient client)
    {
        var request = MakeRequest(HttpMethod.Post, "/matches");
        request.Content = JsonContent.Create(new { });
        var response = await client.SendAsync(request);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        return created.GetProperty("data").GetProperty("id").GetInt32();
    }

    private async Task<PlayerSession> SeatNewPlayer(int id, string name, int matchId)
    {
        var sessionRegistry = _factory.Services.GetRequiredService<IPlayerSessionRegistry>();
        var matchRegistry = _factory.Services.GetRequiredService<IMatchRegistry>();
        var matchMembership = _factory.Services.GetRequiredService<MatchMembershipService>();

        var session = new PlayerSession(id, name, $"token-{id}", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
        sessionRegistry.Add(session);

        var match = matchRegistry.GetByDbId(matchId)!;
        Assert.True(await matchMembership.Join(session, match, ""));
        return session;
    }

    // ---- 404s for a match that isn't currently live ----

    [Theory]
    [InlineData("GET", "/matches/999999/hosts")]
    [InlineData("GET", "/matches/999999/refs")]
    [InlineData("GET", "/matches/999999/ban")]
    [InlineData("GET", "/matches/999999/slots")]
    [InlineData("GET", "/matches/999999/timer")]
    public async Task GetSubResource_UnknownMatch_ReturnsNotFound(string method, string path)
    {
        var response = await _factory.CreateClient().SendAsync(MakeRequest(new HttpMethod(method), path));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PutHosts_MissingAdminKey_ReturnsUnauthorized()
    {
        var request = MakeRequest(HttpMethod.Put, "/matches/1/hosts", adminKey: null);
        request.Content = JsonContent.Create(new { userId = 1 });

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- /hosts ----

    [Fact]
    public async Task Hosts_SetThenClear_ReflectsInGet()
    {
        var client = _factory.CreateClient();
        var matchId = await CreateMatchAsync(client);
        var player = await SeatNewPlayer(2001, "hostcandidate", matchId);

        var putRequest = MakeRequest(HttpMethod.Put, $"/matches/{matchId}/hosts");
        putRequest.Content = JsonContent.Create(new { userId = player.Id });
        var putResponse = await client.SendAsync(putRequest);
        putResponse.EnsureSuccessStatusCode();

        var getResponse = await client.SendAsync(MakeRequest(HttpMethod.Get, $"/matches/{matchId}/hosts"));
        var view = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(player.Id, view.GetProperty("data").GetProperty("host").GetProperty("id").GetInt32());

        var deleteResponse = await client.SendAsync(MakeRequest(HttpMethod.Delete, $"/matches/{matchId}/hosts"));
        deleteResponse.EnsureSuccessStatusCode();

        var afterClear = await client.SendAsync(MakeRequest(HttpMethod.Get, $"/matches/{matchId}/hosts"));
        var clearedView = await afterClear.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(clearedView.GetProperty("data").GetProperty("host").ValueKind is JsonValueKind.Null);
    }

    // ---- /refs ----

    [Fact]
    public async Task Refs_PutToEmpty_ReturnsConflict()
    {
        var client = _factory.CreateClient();
        var matchId = await CreateMatchAsync(client);

        var request = MakeRequest(HttpMethod.Put, $"/matches/{matchId}/refs");
        request.Content = JsonContent.Create(new { userIds = Array.Empty<int>() });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Refs_DeleteLastReferee_ReturnsConflict()
    {
        var client = _factory.CreateClient();
        var matchId = await CreateMatchAsync(client);
        var referee = await SeatNewPlayer(2002, "onlyref", matchId);

        var putRequest = MakeRequest(HttpMethod.Put, $"/matches/{matchId}/refs");
        putRequest.Content = JsonContent.Create(new { userIds = new[] { referee.Id } });
        (await client.SendAsync(putRequest)).EnsureSuccessStatusCode();

        var deleteResponse = await client.SendAsync(
            MakeRequest(HttpMethod.Delete, $"/matches/{matchId}/refs?userId={referee.Id}"));

        Assert.Equal(HttpStatusCode.Conflict, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task Refs_Patch_NeverConflicts()
    {
        var client = _factory.CreateClient();
        var matchId = await CreateMatchAsync(client);
        var referee = await SeatNewPlayer(2003, "patchedref", matchId);

        var request = MakeRequest(HttpMethod.Patch, $"/matches/{matchId}/refs");
        request.Content = JsonContent.Create(new { userIds = new[] { referee.Id } });
        var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        var view = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(view.GetProperty("data").GetProperty("referees").EnumerateArray(),
            r => r.GetProperty("id").GetInt32() == referee.Id);
    }

    // ---- /ban ----

    [Fact]
    public async Task Ban_PutToEmpty_Succeeds_NoGuard()
    {
        var client = _factory.CreateClient();
        var matchId = await CreateMatchAsync(client);

        var request = MakeRequest(HttpMethod.Put, $"/matches/{matchId}/ban");
        request.Content = JsonContent.Create(new { userIds = Array.Empty<int>() });
        var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Ban_PatchThenUnban_ReflectsInGet()
    {
        var client = _factory.CreateClient();
        var matchId = await CreateMatchAsync(client);

        var patchRequest = MakeRequest(HttpMethod.Patch, $"/matches/{matchId}/ban");
        patchRequest.Content = JsonContent.Create(new { userIds = new[] { 555 } });
        (await client.SendAsync(patchRequest)).EnsureSuccessStatusCode();

        var afterBan = await client.SendAsync(MakeRequest(HttpMethod.Get, $"/matches/{matchId}/ban"));
        var bannedView = await afterBan.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(bannedView.GetProperty("data").GetProperty("bannedUsers").EnumerateArray(),
            u => u.GetProperty("id").GetInt32() == 555);

        var unbanResponse = await client.SendAsync(
            MakeRequest(HttpMethod.Delete, $"/matches/{matchId}/ban?userId=555"));
        unbanResponse.EnsureSuccessStatusCode();

        var unbanUnknownResponse = await client.SendAsync(
            MakeRequest(HttpMethod.Delete, $"/matches/{matchId}/ban?userId=555"));
        Assert.Equal(HttpStatusCode.BadRequest, unbanUnknownResponse.StatusCode);
    }

    // ---- /kick ----

    [Fact]
    public async Task Kick_SeatedPlayer_Returns204AndRemovesFromMatch()
    {
        var client = _factory.CreateClient();
        var matchId = await CreateMatchAsync(client);
        var player = await SeatNewPlayer(2004, "kickme", matchId);

        var request = MakeRequest(HttpMethod.Post, $"/matches/{matchId}/kick");
        request.Content = JsonContent.Create(new { userId = player.Id });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(player.Match);
    }

    [Fact]
    public async Task Kick_TargetNotInMatch_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var matchId = await CreateMatchAsync(client);
        var player = await SeatNewPlayer(2005, "elsewhere", matchId);
        var sessionRegistry = _factory.Services.GetRequiredService<IPlayerSessionRegistry>();
        var matchMembership = _factory.Services.GetRequiredService<MatchMembershipService>();
        var matchRegistry = _factory.Services.GetRequiredService<IMatchRegistry>();
        await matchMembership.Leave(player, matchRegistry.GetByDbId(matchId)!);

        var request = MakeRequest(HttpMethod.Post, $"/matches/{matchId}/kick");
        request.Content = JsonContent.Create(new { userId = player.Id });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        _ = sessionRegistry; // keep the DI resolution above self-documenting even though unused after Leave
    }

    // ---- /invite ----

    [Fact]
    public async Task Invite_Force_SeatsBannedTargetIsRejected_ButUnbannedTargetSeated()
    {
        var client = _factory.CreateClient();
        var matchId = await CreateMatchAsync(client);
        var sessionRegistry = _factory.Services.GetRequiredService<IPlayerSessionRegistry>();
        var banned = new PlayerSession(2006, "banned", "t2006", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
        var free = new PlayerSession(2007, "free", "t2007", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
        sessionRegistry.Add(banned);
        sessionRegistry.Add(free);

        var banRequest = MakeRequest(HttpMethod.Patch, $"/matches/{matchId}/ban");
        banRequest.Content = JsonContent.Create(new { userIds = new[] { banned.Id } });
        (await client.SendAsync(banRequest)).EnsureSuccessStatusCode();

        var inviteRequest = MakeRequest(HttpMethod.Post, $"/matches/{matchId}/invite");
        inviteRequest.Content = JsonContent.Create(new { userIds = new[] { banned.Id, free.Id }, force = true });
        var response = await client.SendAsync(inviteRequest);
        response.EnsureSuccessStatusCode();

        var envelope = await response.Content.ReadFromJsonAsync<JsonElement>();
        var byUserId = envelope.GetProperty("data").EnumerateArray().ToDictionary(r => r.GetProperty("userId").GetInt32());

        Assert.False(byUserId[banned.Id].GetProperty("ok").GetBoolean());
        Assert.True(byUserId[free.Id].GetProperty("ok").GetBoolean());
        Assert.Null(banned.Match);
        Assert.NotNull(free.Match);
    }

    // ---- /slots ----

    [Fact]
    public async Task Slots_Get_ReturnsAllSixteenIndicesAsDictKeys()
    {
        var client = _factory.CreateClient();
        var matchId = await CreateMatchAsync(client);

        var response = await client.SendAsync(MakeRequest(HttpMethod.Get, $"/matches/{matchId}/slots"));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var slots = body.GetProperty("data").GetProperty("slots");
        for (var i = 0; i < 16; i++) Assert.True(slots.TryGetProperty(i.ToString(), out _));
    }

    [Fact]
    public async Task Slots_Put_SwapsTwoOccupants()
    {
        var client = _factory.CreateClient();
        var matchId = await CreateMatchAsync(client);
        var a = await SeatNewPlayer(2008, "playerA", matchId);
        var b = await SeatNewPlayer(2009, "playerB", matchId);

        var matchRegistry = _factory.Services.GetRequiredService<IMatchRegistry>();
        var match = matchRegistry.GetByDbId(matchId)!;
        var slotA = match.GetSlotId(a.Id)!.Value;
        var slotB = match.GetSlotId(b.Id)!.Value;

        var request = MakeRequest(HttpMethod.Put, $"/matches/{matchId}/slots");
        request.Content = JsonContent.Create(new
        {
            slots = new Dictionary<string, object>
            {
                [slotA.ToString()] = new { userId = b.Id },
                [slotB.ToString()] = new { userId = a.Id }
            }
        });
        var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        Assert.Equal(b.Id, match.Slots[slotA].PlayerId);
        Assert.Equal(a.Id, match.Slots[slotB].PlayerId);
    }

    [Fact]
    public async Task Slots_Put_UnknownUserId_ReturnsConflict()
    {
        var client = _factory.CreateClient();
        var matchId = await CreateMatchAsync(client);

        var request = MakeRequest(HttpMethod.Put, $"/matches/{matchId}/slots");
        request.Content = JsonContent.Create(new
        {
            slots = new Dictionary<string, object> { ["0"] = new { userId = 999999 } }
        });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Slots_UserIdAndLockedTogether_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var matchId = await CreateMatchAsync(client);
        var player = await SeatNewPlayer(2010, "lockedplayer", matchId);
        var matchRegistry = _factory.Services.GetRequiredService<IMatchRegistry>();
        var slot = matchRegistry.GetByDbId(matchId)!.GetSlotId(player.Id)!.Value;

        var request = MakeRequest(HttpMethod.Patch, $"/matches/{matchId}/slots");
        request.Content = JsonContent.Create(new
        {
            slots = new Dictionary<string, object> { [slot.ToString()] = new { userId = player.Id, locked = true } }
        });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- /timer ----

    [Fact]
    public async Task Timer_StartFalse_SetsRunningTrue_ThenDeleteAborts()
    {
        var client = _factory.CreateClient();
        var matchId = await CreateMatchAsync(client);

        var startRequest = MakeRequest(HttpMethod.Post, $"/matches/{matchId}/timer");
        startRequest.Content = JsonContent.Create(new { seconds = 120 });
        var startResponse = await client.SendAsync(startRequest);
        startResponse.EnsureSuccessStatusCode();
        var afterStart = await startResponse.Content.ReadFromJsonAsync<JsonElement>();
        var afterStartData = afterStart.GetProperty("data");
        Assert.True(afterStartData.GetProperty("running").GetBoolean());
        Assert.False(afterStartData.GetProperty("autoStart").GetBoolean());

        var abortResponse = await client.SendAsync(MakeRequest(HttpMethod.Delete, $"/matches/{matchId}/timer"));
        abortResponse.EnsureSuccessStatusCode();

        var secondAbort = await client.SendAsync(MakeRequest(HttpMethod.Delete, $"/matches/{matchId}/timer"));
        Assert.Equal(HttpStatusCode.Conflict, secondAbort.StatusCode);
    }

    // ---- /abort, /close ----

    [Fact]
    public async Task Abort_NotInProgress_ReturnsConflict()
    {
        var client = _factory.CreateClient();
        var matchId = await CreateMatchAsync(client);

        var response = await client.SendAsync(MakeRequest(HttpMethod.Post, $"/matches/{matchId}/abort"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Close_Returns204AndRemovesFromRegistry()
    {
        var client = _factory.CreateClient();
        var matchId = await CreateMatchAsync(client);

        var response = await client.SendAsync(MakeRequest(HttpMethod.Post, $"/matches/{matchId}/close"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var matchRegistry = _factory.Services.GetRequiredService<IMatchRegistry>();
        Assert.Null(matchRegistry.GetByDbId(matchId));
    }

    /// <summary>Auto-incrementing in-memory stand-in for the Matches/Rounds tables — nothing persisted.</summary>
    private sealed class NoopMatchPersistenceRepository : IMatchPersistenceRepository
    {
        private int _nextId = 1;

        public Task<int> CreateMatchAsync(string name, DateTime createdAt, CancellationToken cancellationToken = default) =>
            Task.FromResult(_nextId++);

        public Task SetMatchEndedAsync(int matchId, DateTime endedAt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<int> CreateRoundAsync(int matchId, int roundIndex, string mapMd5,
            GameMode mode, MatchWinCondition winCondition, MatchTeamType teamType,
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

    /// <summary>
    ///     Stands in for the real DB-backed <see cref="IUserRepository" /> so an offline/unregistered id
    ///     referenced by these tests (e.g. a banned id that was never seated) resolves to "no account" —
    ///     UserBriefResolver's documented fallback — instead of hitting the real SQLite path these tests
    ///     otherwise never need a working database connection for.
    /// </summary>
    private sealed class NoopUserRepository : IUserRepository
    {
        public Task<User?> FetchByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<User?>(null);

        public Task<User?> FetchByNameAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult<User?>(null);

        public Task<string?> FetchPasswordHashAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task UpdateCountryAsync(int id, string country, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UpdatePrivilegesAsync(int id, UserPrivileges priv, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UpdateNameAsync(int id, string name, string safeName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<User?> CreateAsync(string name, string pwBcrypt, string country, UserPrivileges? priv = null,
            CancellationToken cancellationToken = default) => Task.FromResult<User?>(null);

        public Task<IReadOnlyList<User>> FetchAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<User>>([]);
    }
}
