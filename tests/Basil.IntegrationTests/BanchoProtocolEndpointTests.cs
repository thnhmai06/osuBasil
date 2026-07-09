using Basil.Application.Abstractions.Channels;
using Basil.Application.Sessions;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using Basil.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.IntegrationTests;

/// <summary>
///     Ported from app/api/domains/cho.py's bancho_handler: dispatches by presence of the osu-token
///     header. Only the token-present branches are covered here — they touch only the in-memory
///     session registry and dispatcher, no DB. The no-token (login) branch is fully covered by
///     OsuLoginUseCase's own 19 unit tests and is not re-tested through HTTP here.
/// </summary>
public class BanchoProtocolEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public BanchoProtocolEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Server:Domain"] = "test.local",
                    ["Bot:CommandPrefix"] = "!",
                    ["Server:MenuIconPath"] = "icon.png",
                    ["Server:MenuOnclickUrl"] = "https://example.test",
                    ["Database:Path"] = ""
                });
            });
            builder.ConfigureServices(services =>
                services.AddSingleton<IChannelRepository, NullChannelRepository>());
        });
    }

    [Fact]
    public async Task UnknownToken_ReturnsServerRestartedNotification()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/") { Content = new ByteArrayContent([]) };
        request.Headers.Host = "c.test.local";
        request.Headers.Add("osu-token", "does-not-exist");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsByteArrayAsync();

        var expected = ServerPacketWriter.Notification("Server has restarted.")
            .Concat(ServerPacketWriter.RestartServer(0))
            .ToArray();
        Assert.Equal(expected, body);
    }

    [Fact]
    public async Task KnownToken_DispatchesAndReturnsQueuedPackets()
    {
        var sessionRegistry = _factory.Services.GetRequiredService<IPlayerSessionRegistry>();
        var session = new PlayerSession(1, "cmyui", "known-token", Privileges.Unrestricted, 0.0);
        session.Enqueue(ServerPacketWriter.Notification("hello"));
        sessionRegistry.Add(session);

        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/") { Content = new ByteArrayContent([]) };
        request.Headers.Host = "c.test.local";
        request.Headers.Add("osu-token", "known-token");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(ServerPacketWriter.Notification("hello"), body);
    }

    private sealed class NullChannelRepository : IChannelRepository
    {
        public Task<IReadOnlyList<Channel>> FetchAllAutoJoinAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Channel>>([]);
        }

        public Task<Channel?> FetchOneByNameAsync(string name, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Channel?>(null);
        }
    }
}