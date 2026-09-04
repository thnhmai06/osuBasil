using System.Net;
using System.Net.Http.Json;
using Basil.Application.Abstractions.Settings;
using Basil.Application.Configurations;
using Basil.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.IntegrationTests;

/// <summary>
///     Covers `GET`/`PUT /settings/motd`. Backed by a real, stateful
///     <see cref="InMemorySettingsRepository" /> (not a canned substitute) so a `PUT` followed by a
///     `GET` reflects the just-written state, matching how the real Settings-table-backed repository
///     behaves (the same read-your-writes property <see cref="AdminKeyManagementEndpointTests" /> pins).
/// </summary>
public class MotdSettingsManagementEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
	private readonly WebApplicationFactory<Program> _factory;
	private readonly InMemorySettingsRepository _settings = new();

	public MotdSettingsManagementEndpointTests(WebApplicationFactory<Program> factory)
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
				services.AddSingleton<ISettingsRepository>(_settings);
			});
		});
	}

	private static HttpRequestMessage MakeRequest(HttpMethod method, string path)
	{
		return new HttpRequestMessage(method, path) { Headers = { Host = "api.test.local" } };
	}

	// Named for the fake InMemorySettingsRepository's starting state, not a claim about a real
	// deployment: a fresh database's "Motd" row already carries a non-null default (see
	// SqliteSettingsRepositorySeedTests and configuration.md's "Message of the day" section).
	[Fact]
	public async Task GetMotd_EmptyBackingStore_ReturnsNull()
	{
		var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest(HttpMethod.Get, "/settings/motd"));
		var body = await response.Content.ReadAsStringAsync();

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Contains("\"text\":null", body);
	}

	[Fact]
	public async Task SetMotd_ThenGet_ReflectsWrittenValue()
	{
		var client = _factory.CreateClient();

		var setRequest = MakeRequest(HttpMethod.Put, "/settings/motd");
		setRequest.Content = JsonContent.Create(new { text = "Welcome to Basil!" });
		await client.SendAsync(setRequest);

		var response = await client.SendAsync(MakeRequest(HttpMethod.Get, "/settings/motd"));
		var body = await response.Content.ReadAsStringAsync();

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Contains("\"text\":\"Welcome to Basil!\"", body);
	}

	[Fact]
	public async Task SetMotd_EmptyText_ClearsIt()
	{
		var client = _factory.CreateClient();

		var setRequest = MakeRequest(HttpMethod.Put, "/settings/motd");
		setRequest.Content = JsonContent.Create(new { text = "Welcome to Basil!" });
		await client.SendAsync(setRequest);

		var clearRequest = MakeRequest(HttpMethod.Put, "/settings/motd");
		clearRequest.Content = JsonContent.Create(new { text = "" });
		await client.SendAsync(clearRequest);

		var response = await client.SendAsync(MakeRequest(HttpMethod.Get, "/settings/motd"));
		var body = await response.Content.ReadAsStringAsync();

		Assert.Contains("\"text\":null", body);
	}

	[Fact]
	public async Task SetMotd_WithoutAdminKey_Rejected()
	{
		var client = _factory.CreateClient();
		var setRequest = MakeRequest(HttpMethod.Put, "/settings/motd");
		setRequest.Content = JsonContent.Create(new { text = "Welcome to Basil!" });
		var keySetRequest = MakeRequest(HttpMethod.Put, "/settings/adminkey");
		keySetRequest.Content = JsonContent.Create(new { key = "an-admin-key" });
		await client.SendAsync(keySetRequest);

		var response = await client.SendAsync(setRequest);

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	/// <summary>
	///     Unlike its `/settings/` siblings, `GET /settings/motd` is deliberately never gated -- the
	///     message itself isn't sensitive. Pins that with an admin key actually configured, since every
	///     other test in this class runs in bypass mode, where an unauthenticated request would succeed
	///     regardless of whether the route is gated at all.
	/// </summary>
	[Fact]
	public async Task GetMotd_WithAdminKeyConfigured_StillSucceedsWithoutAuthentication()
	{
		var client = _factory.CreateClient();
		var keySetRequest = MakeRequest(HttpMethod.Put, "/settings/adminkey");
		keySetRequest.Content = JsonContent.Create(new { key = "an-admin-key" });
		await client.SendAsync(keySetRequest);

		var response = await client.SendAsync(MakeRequest(HttpMethod.Get, "/settings/motd"));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
	}
}