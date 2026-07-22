using System.Net.Http.Headers;
using System.Text.Json;
using Basil.Application.Configuration;
using Basil.Application.Sessions.Multiplayer;
using Basil.Application.Sessions.Spectating;
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
                services.AddSingleton<IOptions<DatabaseOptions>>(Options.Create(new DatabaseOptions { Path = "" })));
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

        var (eventType, data) = await ReceiveAfterPublishAsync("/spec/7",
            () => events.PublishInput(7, "frame-data"u8.ToArray()));

        Assert.Equal("input", eventType);
        Assert.Equal("frame-data", data);
    }

    [Fact]
    public async Task SpecChannel_IgnoresPublishesForOtherPlayerIds()
    {
        var events = _factory.Services.GetRequiredService<IPlayerInputEvents>();

        var (_, data) = await ReceiveAfterPublishAsync("/spec/7", () =>
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
}
