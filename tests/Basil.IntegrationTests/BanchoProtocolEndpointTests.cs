using Basil.Application.Sessions;
using Basil.Application.Shared.Configuration;
using Basil.Domain.Users;
using Basil.Host;
using Basil.Protocol.Packets;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.IntegrationTests;

/// <summary>
///     Verifies the bancho HTTP endpoint dispatches by presence of the osu-token header. Only the
///     token-present branches are covered here — they touch only the in-memory session registry and
///     dispatcher, no DB. The no-token (login) branch is fully covered by LoginService's own unit
///     tests and is not re-tested through HTTP here.
/// </summary>
public class BanchoProtocolEndpointTests : IClassFixture<WebApplicationFactory<Bootstrap>>
{
	private readonly WebApplicationFactory<Bootstrap> _factory;

	public BanchoProtocolEndpointTests(WebApplicationFactory<Bootstrap> factory)
	{
		_factory = factory.WithWebHostBuilder(builder =>
		{
			builder.ConfigureAppConfiguration((_, config) =>
			{
				config.AddInMemoryCollection(new Dictionary<string, string?>
				{
					["Basil:Server:Domain"] = "test.local",
					["Basil:Bot:CommandPrefix"] = "!"
				});
			});
			builder.ConfigureServices(services =>
			{
				services.AddSingleton(Options.Create(new DatabaseOptions { Path = "" }));
				services.AddSingleton(TestDoubles.BypassAdminKeySettingsRepository());
				services.AddSingleton(TestDoubles.NullChannelRepository());
			});
		});
	}

	[Fact]
	public async Task UnknownToken_ReturnsRestartServerPacket()
	{
		var client = _factory.CreateClient();
		var request = new HttpRequestMessage(HttpMethod.Post, "/") { Content = new ByteArrayContent([]) };
		request.Headers.Host = "c.test.local";
		request.Headers.Add("osu-token", "does-not-exist");

		var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
		var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

		Assert.Equal(ServerPacketWriter.RestartServer(0), body);
	}

	[Fact]
	public async Task KnownToken_DispatchesAndReturnsQueuedPackets()
	{
		var sessionRegistry = _factory.Services.GetRequiredService<ISessionRegistry<GameSession>>();
		var session = new GameSession(1, "cmyui", "known-token", UserPrivileges.Unrestricted,
			DateTimeOffset.UnixEpoch);
		session.Enqueue(ServerPacketWriter.Notification("hello"));
		sessionRegistry.TryAdd(session);

		var client = _factory.CreateClient();
		var request = new HttpRequestMessage(HttpMethod.Post, "/") { Content = new ByteArrayContent([]) };
		request.Headers.Host = "c.test.local";
		request.Headers.Add("osu-token", "known-token");

		var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
		var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

		Assert.Equal(ServerPacketWriter.Notification("hello"), body);
	}
}