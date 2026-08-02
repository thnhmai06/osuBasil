using System.Net;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configurations;
using Basil.Application.Sessions;
using Basil.Domain.Beatmaps;
using Basil.Domain.Users;
using Basil.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.IntegrationTests;

/// <summary>
///     Ported from app/api/domains/osu.py's osuSearchHandler/osuSearchSetHandler, replumbed to query
///     the local beatmaps table instead of a mirror. Only wiring (auth gate, query binding, dispatch to
///     the right formatter) is covered here — DirectSearchService/DirectSearchResponseFormatter have
///     their own unit tests.
/// </summary>
public class DirectSearchEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
	private readonly IBeatmapRepository _beatmaps = Substitute.For<IBeatmapRepository>();
	private readonly WebApplicationFactory<Program> _factory;
	private IReadOnlyList<IReadOnlyList<Beatmap>> _searchResult = [];
	private Beatmap? _setInfo;

	public DirectSearchEndpointTests(WebApplicationFactory<Program> factory)
	{
		_beatmaps.FetchOneAsync(Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<int?>(),
			Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(_ => _setInfo);
		_beatmaps.SearchAsync(Arg.Any<string?>(), Arg.Any<GameMode?>(), Arg.Any<int>(), Arg.Any<int>(),
			Arg.Any<CancellationToken>()).Returns(_ => _searchResult);

		var users = Substitute.For<IUserRepository>();
		users.FetchPasswordHashAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<string?>("stored-hash"));
		users.FetchAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<User>>([]));

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
				services.AddSingleton(TestDoubles.NullChannelRepository());
				services.AddSingleton(users);
				services.AddSingleton(TestDoubles.FixedPasswordHasher());
				services.AddSingleton(_beatmaps);
			});
		});
	}

	private static Beatmap MakeBeatmap(int id, int setId)
	{
		var mapset = new Beatmapset(setId, "Artist", "Title", "cmyui", DateTime.UtcNow, DateTime.UtcNow);
		return new Beatmap(
			new string('0', 32), id, mapset, "Version", "file.osu",
			new Difficulty(GameMode.Standard, 180, TimeSpan.FromSeconds(100), 4, 9, 8, 5, 6.5),
			new OsuBeatmapObjectCounts { MaxCombo = 500 });
	}

	private static HttpRequestMessage MakeRequest(string path, string queryString)
	{
		return new HttpRequestMessage(HttpMethod.Get, $"{path}?{queryString}")
			{ Headers = { Host = "osu.test.local" } };
	}

	[Fact]
	public async Task Search_PlayerNotOnline_ReturnsUnauthorized()
	{
		var request = MakeRequest("/web/osu-search.php", "u=nobody&h=x&r=4&q=Newest&m=-1&p=0");

		var response = await _factory.CreateClient().SendAsync(request);

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Fact]
	public async Task Search_Authenticated_ReturnsFormattedResults()
	{
		var sessionRegistry = _factory.Services.GetRequiredService<IUserSessionRegistry>();
		sessionRegistry.Add(new UserSession(60, "search-user", "tok", UserPrivileges.Unrestricted,
			DateTimeOffset.UnixEpoch));
		_searchResult = [[MakeBeatmap(1, 100)]];
		var request = MakeRequest("/web/osu-search.php", "u=search-user&h=correct-md5&r=4&q=Newest&m=-1&p=0");

		var response = await _factory.CreateClient().SendAsync(request);
		var body = await response.Content.ReadAsStringAsync();

		Assert.StartsWith("1\n100.osz|Artist|Title|cmyui|", body);
	}

	[Fact]
	public async Task SearchSet_PlayerNotOnline_ReturnsUnauthorized()
	{
		var request = MakeRequest("/web/osu-search-set.php", "u=nobody&h=x&s=100");

		var response = await _factory.CreateClient().SendAsync(request);

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Fact]
	public async Task SearchSet_UnknownSet_ReturnsEmptyBody()
	{
		var sessionRegistry = _factory.Services.GetRequiredService<IUserSessionRegistry>();
		sessionRegistry.Add(new UserSession(61, "searchset-unknown", "tok2", UserPrivileges.Unrestricted,
			DateTimeOffset.UnixEpoch));
		_setInfo = null;
		var request = MakeRequest("/web/osu-search-set.php", "u=searchset-unknown&h=correct-md5&s=999");

		var response = await _factory.CreateClient().SendAsync(request);
		var body = await response.Content.ReadAsStringAsync();

		Assert.Equal("", body);
	}

	[Fact]
	public async Task SearchSet_KnownSet_ReturnsFormattedSetLine()
	{
		var sessionRegistry = _factory.Services.GetRequiredService<IUserSessionRegistry>();
		sessionRegistry.Add(new UserSession(62, "searchset-known", "tok3", UserPrivileges.Unrestricted,
			DateTimeOffset.UnixEpoch));
		_setInfo = MakeBeatmap(1, 100);
		var request = MakeRequest("/web/osu-search-set.php", "u=searchset-known&h=correct-md5&s=100");

		var response = await _factory.CreateClient().SendAsync(request);
		var body = await response.Content.ReadAsStringAsync();

		Assert.StartsWith("100.osz|Artist|Title|cmyui|", body);
	}
}