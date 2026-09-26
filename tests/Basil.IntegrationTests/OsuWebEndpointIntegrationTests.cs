using System.Net;
using System.Text;
using Basil.Application.Shared.Configuration;
using Basil.Host;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.IntegrationTests;

/// <summary>
///     Integration tests for the osu-web HTTP endpoints that aren't covered by unit tests
///     (getbeatmapinfo, lastfm, markasread, seasonal, bancho_connect, check-updates,
///     b.* redirect) plus the endpoints deliberately stubbed out (screenshot, favourites, rate,
///     comment, in-game registration, difficulty-rating).
///     Authenticated routes only need "userSession not online" coverage here (no DB access happens before
///     that check — see AuthenticationService); their real logic is unit-tested separately.
/// </summary>
public class OsuWebEndpointIntegrationTests(WebApplicationFactory<Bootstrap> factory)
	: IClassFixture<WebApplicationFactory<Bootstrap>>
{
	private readonly WebApplicationFactory<Bootstrap> _factory = Configure(factory);

	private static WebApplicationFactory<Bootstrap> Configure(WebApplicationFactory<Bootstrap> factory)
	{
		return factory.WithWebHostBuilder(builder =>
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
				services.AddSingleton(TestDoubles.NullMapRepository());
				services.AddSingleton(TestDoubles.NullBeatmapsetRepository());
				services.AddSingleton(TestDoubles.NullUserRepository());
			});
		});
	}

	private static HttpRequestMessage MakeRequest(HttpMethod method, string path, string host = "osu.test.local")
	{
		return new HttpRequestMessage(method, path) { Headers = { Host = host } };
	}

	[Fact]
	public async Task GetBeatmapInfo_PlayerNotOnline_ReturnsUnauthorized()
	{
		var client = _factory.CreateClient();
		var request = MakeRequest(HttpMethod.Post, "/web/osu-getbeatmapinfo.php?u=nobody&h=x");
		request.Content = JsonContent("""{"Filenames":[],"Ids":[]}""");

		var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Fact]
	public async Task LastFm_PlayerNotOnline_ReturnsUnauthorized()
	{
		var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest(HttpMethod.Get, "/web/lastfm.php?b=a0&us=nobody&ha=x"), TestContext.Current.CancellationToken);

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Fact]
	public async Task MarkAsRead_PlayerNotOnline_ReturnsUnauthorized()
	{
		var client = _factory.CreateClient();

		var response =
			await client.SendAsync(MakeRequest(HttpMethod.Get, "/web/osu-markasread.php?u=nobody&h=x"), TestContext.Current.CancellationToken);

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Fact]
	public async Task Seasonal_ReturnsEmptyJsonArray_NoAuthNeeded()
	{
		var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest(HttpMethod.Get, "/web/osu-getseasonal.php"), TestContext.Current.CancellationToken);
		var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal("[]", body);
	}

	[Fact]
	public async Task BanchoConnect_ReturnsEmptyOk_NoAuthNeeded()
	{
		var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest(HttpMethod.Get, "/web/bancho_connect.php?v=b20231231"), TestContext.Current.CancellationToken);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
	}

	[Fact]
	public async Task CheckUpdates_ReturnsEmptyOk_NoAuthNeeded()
	{
		var client = _factory.CreateClient();

		var response =
			await client.SendAsync(MakeRequest(HttpMethod.Get, "/web/check-updates.php?action=check&stream=stable"), TestContext.Current.CancellationToken);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
	}

	[Fact]
	public async Task Screenshot_ReturnsNotAvailable()
	{
		var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest(HttpMethod.Post, "/web/osu-screenshot.php"), TestContext.Current.CancellationToken);
		var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
		Assert.Contains("not available", body);
	}

	[Fact]
	public async Task Favourites_GetAndAdd_ReturnEmptyOk()
	{
		var client = _factory.CreateClient();

		var getResponse = await client.SendAsync(MakeRequest(HttpMethod.Get, "/web/osu-getfavourites.php"), TestContext.Current.CancellationToken);
		var addResponse = await client.SendAsync(MakeRequest(HttpMethod.Get, "/web/osu-addfavourite.php"), TestContext.Current.CancellationToken);

		Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
		Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);
	}

	[Fact]
	public async Task Rate_ReturnsNotRanked()
	{
		var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest(HttpMethod.Get, "/web/osu-rate.php"), TestContext.Current.CancellationToken);
		var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

		Assert.Equal("not ranked", body);
	}

	[Fact]
	public async Task Comment_ReturnsEmptyOk()
	{
		var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest(HttpMethod.Post, "/web/osu-comment.php"), TestContext.Current.CancellationToken);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
	}

	[Fact]
	public async Task Register_BypassMode_SkipsAdminKeyCheck()
	{
		var client = _factory.CreateClient();

		var request = MakeRequest(HttpMethod.Post, "/users");
		request.Content = new StringContent(
			"user[username]=Player1&user[user_email]=anything@test.com&user[password]=hunter2",
			Encoding.UTF8,
			"application/x-www-form-urlencoded");

		var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
		var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal("", body);
	}

	[Fact]
	public async Task Register_InvalidUsername_ReturnsUsernameError()
	{
		var client = _factory.CreateClient();

		var request = MakeRequest(HttpMethod.Post, "/users");
		request.Content = new StringContent(
			"user[username]=ab&user[user_email]=anything@test.com&user[password]=hunter2",
			Encoding.UTF8,
			"application/x-www-form-urlencoded");

		var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
		var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
		Assert.Contains("between 3 and 15 characters", body);
	}

	[Fact]
	public async Task DifficultyRating_NoBeatmapId_ReturnsExplanatoryMessage()
	{
		var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

		var response = await client.SendAsync(MakeRequest(HttpMethod.Post, "/difficulty-rating"), TestContext.Current.CancellationToken);
		var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Contains("beatmap id", body);
	}

	[Fact]
	public async Task DifficultyRating_UnknownBeatmapId_ReturnsNotFound()
	{
		var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

		var response = await client.SendAsync(MakeRequest(HttpMethod.Post, "/difficulty-rating?b=999999999"), TestContext.Current.CancellationToken);

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task BeatmapAssetHost_UnknownBeatmapset_ReturnsNotFound()
	{
		var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

		var response = await client.SendAsync(MakeRequest(HttpMethod.Get, "/thumb/12345l.jpg", "b.test.local"), TestContext.Current.CancellationToken);

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	private static HttpContent JsonContent(string json)
	{
		return new StringContent(json, Encoding.UTF8, "application/json");
	}

	// Database:Path is "" for this test host (no real DB) — /difficulty-rating still calls
	// IBeatmapRepository unconditionally, so it needs a stub rather than a real connection.
}