using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Configuration;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.IntegrationTests;

/// <summary>Covers the two newest SSE channels: GET /match/{id}/live and GET /match/{id}/live/{slotIndex}.</summary>
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
            });
        });
    }

    [Fact]
    public async Task LiveChannel_ReceivesWhateverIsPublishedForThatMatchId()
    {
        var events = _factory.Services.GetRequiredService<IMatchLiveEvents>();

        var (eventType, data) = await ReceiveAfterPublishAsync("/match/5/live",
            () => events.PublishLive(5, JsonSerializer.SerializeToUtf8Bytes(new { inProgress = true })));

        Assert.Equal("live", eventType);
        Assert.Contains("true", data);
    }

    [Fact]
    public async Task LiveSlotChannel_UnknownMatch_ReturnsNotFound()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/match/999999/live/1") { Headers = { Host = "api.test.local" } };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task LiveSlotChannel_ReceivesSlotEventsForItsOwnSlotOnly()
    {
        var client = _factory.CreateClient();
        var createRequest = new HttpRequestMessage(HttpMethod.Post, "/match") { Headers = { Host = "api.test.local" } };
        createRequest.Headers.Add("X-Admin-Key", AdminKey);
        createRequest.Content = JsonContent.Create(new { });
        var createResponse = await client.SendAsync(createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var matchId = created.GetProperty("id").GetInt32();

        var events = _factory.Services.GetRequiredService<IMatchLiveEvents>();

        var (eventType, data) = await ReceiveAfterPublishAsync($"/match/{matchId}/live/1", () =>
        {
            events.PublishSlot(matchId, 5, "wrong slot"u8.ToArray());
            events.PublishSlot(matchId, 0, "right slot"u8.ToArray());
        });

        Assert.Equal("slot", eventType);
        Assert.Equal("right slot", data);
    }

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

    private sealed class NoopMatchPersistenceRepository : IMatchPersistenceRepository
    {
        private int _nextId = 1;

        public Task<int> CreateMatchAsync(string name, DateTime createdAt, CancellationToken cancellationToken = default) =>
            Task.FromResult(_nextId++);

        public Task SetMatchEndedAsync(int matchId, DateTime endedAt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

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
