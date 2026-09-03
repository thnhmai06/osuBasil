using System.Net;
using System.Net.Http.Json;
using Basil.Application.Configurations;
using Basil.Application.Sessions;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using Basil.Web;
using Basil.Web.OpenApi;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.IntegrationTests;

/// <summary>
///     Covers `POST /announce`: pushes a notification popup to online players, excluding BasilBot,
///     targeting either everyone online (`userIds` omitted) or an explicit id list.
/// </summary>
public class AnnounceEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
	private const string AdminKey = "correct-key";
	private readonly WebApplicationFactory<Program> _factory;

	public AnnounceEndpointTests(WebApplicationFactory<Program> factory)
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
				services.AddSingleton(TestDoubles.FixedAdminKeySettingsRepository());
			});
		});
	}

	private static GameSession MakeSession(int id, string name, bool isBot = false)
	{
		return new GameSession(id, name, $"token-{id}", UserPrivileges.Unrestricted, DateTimeOffset.UtcNow)
			{ IsBot = isBot };
	}

	private static HttpRequestMessage MakeRequest(object body, string? adminKey = AdminKey)
	{
		var request = new HttpRequestMessage(HttpMethod.Post, "/announce") { Headers = { Host = "api.test.local" } };
		if (adminKey is not null) request.Headers.Add("Authorization", $"Bearer {adminKey}");
		request.Content = JsonContent.Create(body);
		return request;
	}

	[Fact]
	public async Task Announce_NoUserIds_NotifiesEveryoneOnlineExceptBot()
	{
		var client = _factory.CreateClient();
		var registry = _factory.Services.GetRequiredService<ISessionRegistry<GameSession>>();
		var alice = MakeSession(1, "alice");
		var bob = MakeSession(2, "bob");
		var bot = MakeSession(0, "BasilBot", true);
		registry.TryAdd(alice);
		registry.TryAdd(bob);
		registry.TryAdd(bot);

		var response = await client.SendAsync(MakeRequest(new { text = "server restarting soon" }));
		var body = await response.Content.ReadFromJsonAsync<Envelope<AnnounceResultData>>();

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.NotNull(body?.Data);
		Assert.Equal(2, body.Data!.DeliveredCount);

		var expectedPacket = ServerPacketWriter.Notification("server restarting soon");
		Assert.Equal(expectedPacket, alice.Dequeue());
		Assert.Equal(expectedPacket, bob.Dequeue());
		Assert.Empty(bot.Dequeue());
	}

	[Fact]
	public async Task Announce_ExplicitUserIds_NotifiesOnlyThoseSkippingOfflineAndBot()
	{
		var client = _factory.CreateClient();
		var registry = _factory.Services.GetRequiredService<ISessionRegistry<GameSession>>();
		var alice = MakeSession(11, "alice2");
		registry.TryAdd(alice);

		var response = await client.SendAsync(MakeRequest(new { text = "hi", userIds = new[] { 11, 999, 0 } }));
		var body = await response.Content.ReadFromJsonAsync<Envelope<AnnounceResultData>>();

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal(1, body!.Data!.DeliveredCount);
		Assert.Equal(ServerPacketWriter.Notification("hi"), alice.Dequeue());
	}

	[Fact]
	public async Task Announce_EmptyText_ReturnsBadRequest()
	{
		var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest(new { text = "" }));

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}

	[Fact]
	public async Task Announce_MissingAdminKey_ReturnsUnauthorized()
	{
		var response = await _factory.CreateClient().SendAsync(MakeRequest(new { text = "hi" }, null));

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	private sealed record AnnounceResultData(int DeliveredCount);
}