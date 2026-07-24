using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Basil.Application.Abstractions.Multiplayer;
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
///     Covers the five new SSE channels added alongside the `/hosts`, `/refs`, `/ban`, `/slots`, and
///     `/timer` sub-resource routes — full-then-delta, same pattern as
///     <see cref="MatchLiveChannelsEndpointTests" />. Each test connects the stream first, then
///     drives the corresponding write route (through real DI-resolved production singletons, exactly
///     like <see cref="MatchSubResourceEndpointTests" />) and asserts the resulting delta.
/// </summary>
public class MatchSubResourceSseEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AdminKey = "correct-key";
    private readonly WebApplicationFactory<Program> _factory;

    public MatchSubResourceSseEndpointTests(WebApplicationFactory<Program> factory)
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
        return created.GetProperty("id").GetInt32();
    }

    private PlayerSession SeatNewPlayer(int id, string name, int matchId)
    {
        var sessionRegistry = _factory.Services.GetRequiredService<IPlayerSessionRegistry>();
        var matchRegistry = _factory.Services.GetRequiredService<IMatchRegistry>();
        var matchMembership = _factory.Services.GetRequiredService<MatchMembershipService>();

        var session = new PlayerSession(id, name, $"token-{id}", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
        sessionRegistry.Add(session);

        var match = matchRegistry.GetByDbId(matchId)!;
        Assert.True(matchMembership.Join(session, match, ""));
        return session;
    }

    /// <summary>
    ///     Connects an SSE stream and reads its first event, then performs <paramref name="trigger" />
    ///     and reads the event after that. The channel must already have a non-null
    ///     <see cref="SnapshotChannel{T}.Latest" /> before connecting (callers warm it up with one
    ///     preliminary write) — otherwise the connect's "subscribe, drain, snapshot" sequence has
    ///     nothing to write immediately, and per the existing note on
    ///     <see cref="MatchLiveChannelsEndpointTests" />'s own helper ("an SSE response apparently
    ///     doesn't flush its headers until its first write"), awaiting the connect before ever
    ///     publishing anything would deadlock. With a warm channel, the connect's first event is that
    ///     warm full snapshot, and <paramref name="trigger" /> then produces a real delta as the second.
    /// </summary>
    private async Task<(string? EventType, string Data)> ReceiveAfterTriggerAsync(string path, Func<Task> trigger)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, path) { Headers = { Host = "api.test.local" } };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);

        await ReadNextEventAsync(reader, cts.Token); // discard the warm full snapshot
        await trigger();
        return await ReadNextEventAsync(reader, cts.Token);
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

    [Fact]
    public async Task Hosts_Sse_DeltaFiresOnSetHost()
    {
        var client = _factory.CreateClient();
        var matchId = await CreateMatchAsync(client);
        var warmHost = SeatNewPlayer(3000, "warmhost", matchId);
        var player = SeatNewPlayer(3001, "hostcandidate", matchId);

        // Warm HostSnapshot.Latest so the SSE connect below has an immediate full snapshot to write —
        // see ReceiveAfterTriggerAsync's doc comment for why an unwarmed channel would deadlock.
        var warmRequest = MakeRequest(HttpMethod.Put, $"/matches/{matchId}/hosts");
        warmRequest.Content = JsonContent.Create(new { userId = warmHost.Id });
        (await client.SendAsync(warmRequest)).EnsureSuccessStatusCode();

        var (eventType, data) = await ReceiveAfterTriggerAsync($"/matches/{matchId}/hosts", async () =>
        {
            var request = MakeRequest(HttpMethod.Put, $"/matches/{matchId}/hosts");
            request.Content = JsonContent.Create(new { userId = player.Id });
            (await client.SendAsync(request)).EnsureSuccessStatusCode();
        });

        Assert.Equal("hosts", eventType);
        Assert.Contains(player.Id.ToString(), data);
    }

    [Fact]
    public async Task Refs_Sse_DeltaFiresOnAddReferee()
    {
        var client = _factory.CreateClient();
        var matchId = await CreateMatchAsync(client);
        var warmRef = SeatNewPlayer(3010, "warmref", matchId);
        var referee = SeatNewPlayer(3002, "newref", matchId);

        var warmRequest = MakeRequest(HttpMethod.Patch, $"/matches/{matchId}/refs");
        warmRequest.Content = JsonContent.Create(new { userIds = new[] { warmRef.Id } });
        (await client.SendAsync(warmRequest)).EnsureSuccessStatusCode();

        var (eventType, data) = await ReceiveAfterTriggerAsync($"/matches/{matchId}/refs", async () =>
        {
            var request = MakeRequest(HttpMethod.Patch, $"/matches/{matchId}/refs");
            request.Content = JsonContent.Create(new { userIds = new[] { referee.Id } });
            (await client.SendAsync(request)).EnsureSuccessStatusCode();
        });

        Assert.Equal("refs", eventType);
        Assert.Contains(referee.Id.ToString(), data);
    }

    [Fact]
    public async Task Ban_Sse_DeltaFiresOnBan()
    {
        var client = _factory.CreateClient();
        var matchId = await CreateMatchAsync(client);

        var warmRequest = MakeRequest(HttpMethod.Patch, $"/matches/{matchId}/ban");
        warmRequest.Content = JsonContent.Create(new { userIds = new[] { 111 } });
        (await client.SendAsync(warmRequest)).EnsureSuccessStatusCode();

        var (eventType, data) = await ReceiveAfterTriggerAsync($"/matches/{matchId}/ban", async () =>
        {
            var request = MakeRequest(HttpMethod.Patch, $"/matches/{matchId}/ban");
            request.Content = JsonContent.Create(new { userIds = new[] { 777 } });
            (await client.SendAsync(request)).EnsureSuccessStatusCode();
        });

        Assert.Equal("ban", eventType);
        Assert.Contains("777", data);
    }

    [Fact]
    public async Task Slots_Sse_DeltaFiresOnReassignment()
    {
        var client = _factory.CreateClient();
        var matchId = await CreateMatchAsync(client);
        var player = SeatNewPlayer(3003, "mover", matchId);
        var matchRegistry = _factory.Services.GetRequiredService<IMatchRegistry>();
        var match = matchRegistry.GetByDbId(matchId)!;
        var currentSlot = match.GetSlotId(player.Id)!.Value;
        var otherSlot = currentSlot == 0 ? 1 : 0;

        // Warm SlotsSnapshot.Latest with a no-op re-team of the player's own current slot.
        var warmRequest = MakeRequest(HttpMethod.Patch, $"/matches/{matchId}/slots");
        warmRequest.Content = JsonContent.Create(new
        {
            slots = new Dictionary<string, object> { [currentSlot.ToString()] = new { userId = player.Id } }
        });
        (await client.SendAsync(warmRequest)).EnsureSuccessStatusCode();

        var (eventType, data) = await ReceiveAfterTriggerAsync($"/matches/{matchId}/slots", async () =>
        {
            var request = MakeRequest(HttpMethod.Patch, $"/matches/{matchId}/slots");
            request.Content = JsonContent.Create(new
            {
                slots = new Dictionary<string, object> { [otherSlot.ToString()] = new { userId = player.Id } }
            });
            (await client.SendAsync(request)).EnsureSuccessStatusCode();
        });

        Assert.Equal("slots", eventType);
        Assert.Contains(player.Id.ToString(), data);
    }

    [Fact]
    public async Task Timer_Sse_DeltaFiresOnAbort()
    {
        var client = _factory.CreateClient();
        var matchId = await CreateMatchAsync(client);

        // Warm TimerSnapshot.Latest by starting a countdown (BeginCountdown publishes synchronously
        // before the POST response returns).
        var warmRequest = MakeRequest(HttpMethod.Post, $"/matches/{matchId}/timer");
        warmRequest.Content = JsonContent.Create(new { seconds = 120 });
        (await client.SendAsync(warmRequest)).EnsureSuccessStatusCode();

        var (eventType, data) = await ReceiveAfterTriggerAsync($"/matches/{matchId}/timer",
            async () => (await client.SendAsync(MakeRequest(HttpMethod.Delete, $"/matches/{matchId}/timer")))
                .EnsureSuccessStatusCode());

        Assert.Equal("timer", eventType);
        Assert.Contains("\"running\":false", data);
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
}
