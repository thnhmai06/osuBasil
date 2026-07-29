using System.Net;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Configuration;
using Basil.Domain.Beatmaps;
using Basil.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.IntegrationTests;

/// <summary>
///     bancho.py routes by hostname (app/api/init_api.py:init_routes) rather than by path prefix:
///     c./ce./c4./c5./c6.{domain} -> bancho realtime, osu.{domain} -> osu! web endpoints,
///     b.{domain} -> beatmap assets, api.{domain} -> developer API. Both the configured DOMAIN and
///     the hardcoded ppy.sh are registered for every group. This test locks in that routing shape
///     before any real endpoint logic exists.
/// </summary>
public class HostRoutingTests : IClassFixture<WebApplicationFactory<Program>>
{
	private readonly WebApplicationFactory<Program> _factory;

	public HostRoutingTests(WebApplicationFactory<Program> factory)
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
				services.AddSingleton(TestDoubles.NullMapsetRepository());
			});
		});
	}

	[Theory]
	[InlineData("c.test.local")]
	[InlineData("ce.test.local")]
	[InlineData("c4.test.local")]
	[InlineData("c5.test.local")]
	[InlineData("c6.test.local")]
	[InlineData("c.ppy.sh")]
	[InlineData("ce.ppy.sh")]
	[InlineData("c4.ppy.sh")]
	[InlineData("c5.ppy.sh")]
	[InlineData("c6.ppy.sh")]
	public async Task BanchoSubdomains_RouteToChoGroup(string host)
	{
		var client = _factory.CreateClient();
		var response = await SendWithHost(client, host);

		response.EnsureSuccessStatusCode();
		Assert.Equal("cho", await response.Content.ReadAsStringAsync());
	}

	[Theory]
	[InlineData("osu.test.local")]
	[InlineData("osu.ppy.sh")]
	public async Task OsuSubdomain_RoutesToOsuWebGroup(string host)
	{
		var client = _factory.CreateClient();
		var response = await SendWithHost(client, host);

		response.EnsureSuccessStatusCode();
		Assert.Equal("osu", await response.Content.ReadAsStringAsync());
	}

	[Theory]
	[InlineData("b.test.local")]
	[InlineData("b.ppy.sh")]
	public async Task BeatmapAssetSubdomain_UnknownMapset_ReturnsNotFound(string host)
	{
		var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
		var request = new HttpRequestMessage(HttpMethod.Get, "/thumb/1l.jpg");
		request.Headers.Host = host;
		var response = await client.SendAsync(request);

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Theory]
	[InlineData("b.test.local")]
	[InlineData("b.ppy.sh")]
	public async Task BeatmapAssetSubdomain_KnownMapset_RedirectsToApiBackground(string host)
	{
		var mapset = new Mapset(1, "Artist", "Title", "Creator", DateTime.UtcNow, DateTime.UtcNow);
		var mapsets = Substitute.For<IMapsetRepository>();
		mapsets.FetchByIdAsync(1, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Mapset?>(mapset));

		var factory = _factory.WithWebHostBuilder(builder =>
			builder.ConfigureServices(services => services.AddSingleton(mapsets)));
		var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
		var request = new HttpRequestMessage(HttpMethod.Get, "/thumb/1.jpg");
		request.Headers.Host = host;
		var response = await client.SendAsync(request);

		Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
		Assert.Equal("https://api.test.local/beatmapsets/1/background", response.Headers.Location!.ToString());
	}

	[Theory]
	[InlineData("api.test.local")]
	[InlineData("api.ppy.sh")]
	public async Task ApiSubdomain_RoutesToApiGroup(string host)
	{
		var client = _factory.CreateClient();
		var response = await SendWithHost(client, host);

		response.EnsureSuccessStatusCode();
		// "/" now serves the generated OpenAPI/Scalar docs site landing page instead of a bare stub.
		Assert.Contains("Basil API Documentation", await response.Content.ReadAsStringAsync());
	}

	[Fact]
	public async Task UnrecognizedHost_Returns404()
	{
		var client = _factory.CreateClient();
		var response = await SendWithHost(client, "unknown.test.local");

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	private static async Task<HttpResponseMessage> SendWithHost(HttpClient client, string host)
	{
		var request = new HttpRequestMessage(HttpMethod.Get, "/");
		request.Headers.Host = host;
		return await client.SendAsync(request);
	}
}