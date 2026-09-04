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
///     Covers `GET`/`PUT /settings/mirror`. Backed by a real, stateful
///     <see cref="InMemorySettingsRepository" /> (not a canned substitute) so a `PUT` followed by a
///     `GET` reflects the just-written state, matching how the real Settings-table-backed repository
///     behaves (the same read-your-writes property <see cref="AdminKeyManagementEndpointTests" /> pins).
/// </summary>
public class MirrorSettingsManagementEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
	private readonly WebApplicationFactory<Program> _factory;
	private readonly InMemorySettingsRepository _settings = new();

	public MirrorSettingsManagementEndpointTests(WebApplicationFactory<Program> factory)
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

	[Fact]
	public async Task GetMirror_Unconfigured_ReturnsBothNull()
	{
		var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest(HttpMethod.Get, "/settings/mirror"));
		var body = await response.Content.ReadAsStringAsync();

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Contains("\"downloadEndpoint\":null", body);
		Assert.Contains("\"searchEndpoint\":null", body);
	}

	[Fact]
	public async Task SetMirror_ThenGet_ReflectsWrittenValues()
	{
		var client = _factory.CreateClient();

		var setRequest = MakeRequest(HttpMethod.Put, "/settings/mirror");
		setRequest.Content =
			JsonContent.Create(new { downloadEndpoint = "https://mirror.local/d", searchEndpoint = "https://mirror.local/s" });
		await client.SendAsync(setRequest);

		var response = await client.SendAsync(MakeRequest(HttpMethod.Get, "/settings/mirror"));
		var body = await response.Content.ReadAsStringAsync();

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Contains("\"downloadEndpoint\":\"https://mirror.local/d\"", body);
		Assert.Contains("\"searchEndpoint\":\"https://mirror.local/s\"", body);
	}

	[Fact]
	public async Task SetMirror_OmittingAField_ClearsThatEndpoint()
	{
		var client = _factory.CreateClient();

		var setRequest = MakeRequest(HttpMethod.Put, "/settings/mirror");
		setRequest.Content =
			JsonContent.Create(new { downloadEndpoint = "https://mirror.local/d", searchEndpoint = "https://mirror.local/s" });
		await client.SendAsync(setRequest);

		var clearRequest = MakeRequest(HttpMethod.Put, "/settings/mirror");
		clearRequest.Content = JsonContent.Create(new { downloadEndpoint = "https://mirror.local/d" });
		await client.SendAsync(clearRequest);

		var response = await client.SendAsync(MakeRequest(HttpMethod.Get, "/settings/mirror"));
		var body = await response.Content.ReadAsStringAsync();

		Assert.Contains("\"downloadEndpoint\":\"https://mirror.local/d\"", body);
		Assert.Contains("\"searchEndpoint\":null", body);
	}
}
