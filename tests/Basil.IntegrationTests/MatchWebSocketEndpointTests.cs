using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Basil.Application.Sessions.Multiplayer;
using Basil.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.IntegrationTests;

/// <summary>
///     Covers the live WS layer (Phase C's keystone slice) end to end over TestServer's in-memory
///     WebSocket transport — no real osu! client or tourney manager involved, but a real WS
///     handshake, a real IMatchEventBus publish, and a real socket receive. Publishes are retried in
///     a short poll loop rather than fired once: a client's WS handshake completing does not
///     guarantee the server-side handler has reached IMatchEventBus.Subscribe* yet (both run
///     in-process with no real network latency between them, so this race is easy to lose without
///     the retry).
/// </summary>
public class MatchWebSocketEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public MatchWebSocketEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ServerBehavior:Domain"] = "test.local",
                    ["Bot:CommandPrefix"] = "!",
                    ["ServerBehavior:MenuIconPath"] = "icon.png",
                    ["ServerBehavior:MenuOnclickUrl"] = "https://example.test",
                    ["Database:Path"] = ""
                });
            });
        });
    }

    [Fact]
    public async Task MainChannel_ReceivesWhateverIsPublishedForThatMatchId()
    {
        var socket = await ConnectAsync("/multi/5");
        var eventBus = _factory.Services.GetRequiredService<IMatchEventBus>();

        var received = await ReceiveAfterPublishAsync(socket,
            () => eventBus.PublishMain(5, JsonSerializer.SerializeToUtf8Bytes(new { hello = "world" })));

        Assert.Contains("world", received);
        socket.Dispose();
    }

    [Fact]
    public async Task InputChannel_ReceivesWhateverIsPublishedForThatMatchId()
    {
        var socket = await ConnectAsync("/multi/7/input");
        var eventBus = _factory.Services.GetRequiredService<IMatchEventBus>();

        var received = await ReceiveAfterPublishAsync(socket,
            () => eventBus.PublishInput(7, "frame-data"u8.ToArray()));

        Assert.Equal("frame-data", received);
        socket.Dispose();
    }

    [Fact]
    public async Task PlayerChannel_OnlyReceivesPublishesForThatPlayerName()
    {
        var socket = await ConnectAsync("/multi/9/alice");
        var eventBus = _factory.Services.GetRequiredService<IMatchEventBus>();

        var received = await ReceiveAfterPublishAsync(socket, () =>
        {
            eventBus.PublishPlayer(9, "bob", "not for alice"u8.ToArray());
            eventBus.PublishPlayer(9, "alice", "for alice"u8.ToArray());
        });

        Assert.Equal("for alice", received);
        socket.Dispose();
    }

    [Fact]
    public async Task MainChannel_OnlyReceivesPublishesForItsOwnMatchId_NotOtherMatches()
    {
        var socket = await ConnectAsync("/multi/11");
        var eventBus = _factory.Services.GetRequiredService<IMatchEventBus>();

        var received = await ReceiveAfterPublishAsync(socket, () =>
        {
            eventBus.PublishMain(12, "wrong match"u8.ToArray());
            eventBus.PublishMain(11, "right match"u8.ToArray());
        });

        Assert.Equal("right match", received);
        socket.Dispose();
    }

    private async Task<WebSocket> ConnectAsync(string path)
    {
        var client = _factory.Server.CreateWebSocketClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await client.ConnectAsync(new Uri($"ws://api.test.local{path}"), cts.Token);
    }

    /// <summary>Calls <paramref name="publish" /> repeatedly (every 50ms) until a message arrives or 10s elapse.</summary>
    private static async Task<string> ReceiveAfterPublishAsync(WebSocket socket, Action publish)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer = new byte[4096];
        var receiveTask = socket.ReceiveAsync(buffer, cts.Token);

        while (!receiveTask.IsCompleted)
        {
            publish();
            await Task.WhenAny(receiveTask, Task.Delay(50, CancellationToken.None));
        }

        var result = await receiveTask;
        return Encoding.UTF8.GetString(buffer, 0, result.Count);
    }
}