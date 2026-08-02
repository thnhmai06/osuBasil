using Basil.Application.Configurations;
using Basil.Application.Sessions;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using Basil.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.IntegrationTests;

/// <summary>
///     Ported from app/api/domains/cho.py's bancho_handler: dispatches by presence of the osu-token
///     header. Only the token-present branches are covered here — they touch only the in-memory
///     session registry and dispatcher, no DB. The no-token (login) branch is fully covered by
///     LoginService's own 19 unit tests and is not re-tested through HTTP here.
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
					["Basil:Server:Domain"] = "test.local",
					["Basil:Bot:CommandPrefix"] = "!"
				});
			});
			builder.ConfigureServices(services =>
			{
				services.AddSingleton<IOptions<DatabaseOptions>>(Options.Create(new DatabaseOptions { Path = "" }));
				services.AddSingleton(TestDoubles.NullChannelRepository());
			});
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
		var sessionRegistry = _factory.Services.GetRequiredService<IUserSessionRegistry>();
		var session = new UserSession(1, "cmyui", "known-token", UserPrivileges.Unrestricted,
			DateTimeOffset.UnixEpoch);
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
}