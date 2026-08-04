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
///     Covers `GET`/`PUT`/`DELETE /adminkey`. Backed by a real, stateful
///     <see cref="InMemorySettingsRepository" /> (not a canned substitute) so a `PUT` followed by a
///     `GET`/further request reflects the just-written state, matching how the real
///     Settings-table-backed repository behaves.
/// </summary>
public class AdminKeyManagementEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
	private readonly InMemorySettingsRepository _settings = new();
	private readonly WebApplicationFactory<Program> _factory;

	public AdminKeyManagementEndpointTests(WebApplicationFactory<Program> factory)
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

	private static HttpRequestMessage MakeRequest(HttpMethod method, string path, string? adminKey = null)
	{
		var request = new HttpRequestMessage(method, path) { Headers = { Host = "api.test.local" } };
		if (adminKey is not null) request.Headers.Add("Authorization", $"Bearer {adminKey}");
		return request;
	}

	[Fact]
	public async Task GetStatus_BypassMode_ReturnsNullLastChanged()
	{
		var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest(HttpMethod.Get, "/adminkey"));
		var body = await response.Content.ReadAsStringAsync();

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Contains("\"lastChanged\":null", body);
	}

	[Fact]
	public async Task SetKey_BypassMode_SucceedsWithoutAnyKey()
	{
		var client = _factory.CreateClient();

		var request = MakeRequest(HttpMethod.Put, "/adminkey");
		request.Content = JsonContent.Create(new { key = "new-secret" });

		var response = await client.SendAsync(request);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
	}

	[Fact]
	public async Task SetKey_ThenOldBypassRequestsAreRejected_NewKeyWorks()
	{
		var client = _factory.CreateClient();

		var setRequest = MakeRequest(HttpMethod.Put, "/adminkey");
		setRequest.Content = JsonContent.Create(new { key = "new-secret" });
		await client.SendAsync(setRequest);

		var withoutKey = await client.SendAsync(MakeRequest(HttpMethod.Get, "/adminkey"));
		Assert.Equal(HttpStatusCode.Unauthorized, withoutKey.StatusCode);

		var withNewKey = await client.SendAsync(MakeRequest(HttpMethod.Get, "/adminkey", "new-secret"));
		Assert.Equal(HttpStatusCode.OK, withNewKey.StatusCode);
	}

	[Fact]
	public async Task SetKey_TooLong_ReturnsBadRequest()
	{
		var client = _factory.CreateClient();

		var request = MakeRequest(HttpMethod.Put, "/adminkey");
		request.Content = JsonContent.Create(new { key = new string('a', 73) });

		var response = await client.SendAsync(request);

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}

	[Fact]
	public async Task DeleteKey_AfterSet_ReturnsToBypassMode()
	{
		var client = _factory.CreateClient();

		var setRequest = MakeRequest(HttpMethod.Put, "/adminkey");
		setRequest.Content = JsonContent.Create(new { key = "new-secret" });
		await client.SendAsync(setRequest);

		var deleteResponse = await client.SendAsync(MakeRequest(HttpMethod.Delete, "/adminkey", "new-secret"));
		Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

		var afterDelete = await client.SendAsync(MakeRequest(HttpMethod.Get, "/adminkey"));
		Assert.Equal(HttpStatusCode.OK, afterDelete.StatusCode);
	}
}
