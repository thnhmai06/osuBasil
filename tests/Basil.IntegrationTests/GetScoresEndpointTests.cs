using System.Net;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Scores;
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
///     Ported from app/api/domains/osu.py's getScores, reduced to a status-only reply — per-beatmap
///     leaderboard browsing is out of scope (see BanchoHostGroups.cs's route doc comment), but the
///     map's real BeatmapStatus is still reported via the stubbed <see cref="IBeatmapRepository" />. Covers
///     the auth gate, the mode/mods status-broadcast side effect (this is the only request osu! sends
///     on every song-select map change), and the two status outcomes (known/unknown map).
/// </summary>
public class GetScoresEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
	public const string KnownMd5 = "known-md5";

	private static readonly Beatmapset Beatmapset = new(1, "Artist", "Title", "Creator", DateTime.UnixEpoch,
		DateTime.UnixEpoch);

	private static readonly Beatmap Beatmap = new(
		KnownMd5, 1, Beatmapset, "Normal", "map.osu",
		new Difficulty(GameMode.Standard, 0, TimeSpan.Zero, 0, 0, 0, 0, 0), new OsuBeatmapObjectCounts());

	private readonly WebApplicationFactory<Program> _factory;

	public GetScoresEndpointTests(WebApplicationFactory<Program> factory)
	{
		var users = Substitute.For<IUserRepository>();
		users.FetchPasswordHashAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<string?>("stored-hash"));
		users.FetchAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<User>>([]));

		var maps = Substitute.For<IBeatmapRepository>();
		maps.FetchOneAsync(Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<int?>(),
				Arg.Any<bool>(), Arg.Any<CancellationToken>())
			.Returns(call => call.ArgAt<string?>(1) == KnownMd5 ? Beatmap : null);
		maps.SearchAsync(Arg.Any<string?>(), Arg.Any<GameMode?>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IReadOnlyList<IReadOnlyList<Beatmap>>>([]));
		maps.FetchAllBySetIdAsync(Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IReadOnlyList<Beatmap>>([]));

		var scores = Substitute.For<IScoreRepository>();
		scores.FetchPageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IReadOnlyList<ScoreRow>>([]));
		scores.FetchByRoundAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IReadOnlyList<ScoreReport>>([]));

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
				services.AddSingleton(users);
				services.AddSingleton(TestDoubles.FixedPasswordHasher());
				services.AddSingleton(scores);
				services.AddSingleton(maps);
			});
		});
	}

	private static HttpRequestMessage MakeRequest(string queryString)
	{
		return new HttpRequestMessage(HttpMethod.Get, $"/web/osu-osz2-getscores.php?{queryString}")
			{ Headers = { Host = "osu.test.local" } };
	}

	[Fact]
	public async Task PlayerNotOnline_ReturnsUnauthorized()
	{
		var client = _factory.CreateClient();
		var request = MakeRequest("us=nobody&ha=x&m=0&mods=0");

		var response = await client.SendAsync(request);

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Fact]
	public async Task WrongPassword_ReturnsUnauthorized()
	{
		var sessionRegistry = _factory.Services.GetRequiredService<IUserSessionRegistry>();
		sessionRegistry.TryAddGameSession(new GameSession(50, "cmyui-wrongpw", "tok", UserPrivileges.Unrestricted,
			DateTimeOffset.UnixEpoch));
		var request = MakeRequest("us=cmyui-wrongpw&ha=wrong-md5&m=0&mods=0");

		var response = await _factory.CreateClient().SendAsync(request);

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Fact]
	public async Task Authenticated_UnknownMap_ReturnsNotSubmitted()
	{
		var sessionRegistry = _factory.Services.GetRequiredService<IUserSessionRegistry>();
		sessionRegistry.TryAddGameSession(new GameSession(51, "cmyui-stub", "tok2", UserPrivileges.Unrestricted,
			DateTimeOffset.UnixEpoch));
		var request = MakeRequest("us=cmyui-stub&ha=correct-md5&c=unknown-md5&m=0&mods=0");

		var response = await _factory.CreateClient().SendAsync(request);
		var body = await response.Content.ReadAsStringAsync();

		Assert.Equal("-1|false", body);
	}

	[Fact]
	public async Task Authenticated_KnownMap_ReturnsMapsetRankedStatus()
	{
		var sessionRegistry = _factory.Services.GetRequiredService<IUserSessionRegistry>();
		sessionRegistry.TryAddGameSession(new GameSession(56, "cmyui-known", "tok5", UserPrivileges.Unrestricted,
			DateTimeOffset.UnixEpoch));
		var request = MakeRequest($"us=cmyui-known&ha=correct-md5&c={KnownMd5}&m=0&mods=0");

		var response = await _factory.CreateClient().SendAsync(request);
		var body = await response.Content.ReadAsStringAsync();

		Assert.Equal($"{(int)BeatmapStatus.Approved}|false", body);
	}

	[Fact]
	public async Task ModeOrModsChanged_BroadcastsUpdatedStatsToOtherSessions()
	{
		var sessionRegistry = _factory.Services.GetRequiredService<IUserSessionRegistry>();
		var player = new GameSession(52, "cmyui-status", "tok3", UserPrivileges.Unrestricted,
			DateTimeOffset.UnixEpoch);
		var other = new GameSession(53, "other", "other-token", UserPrivileges.Unrestricted,
			DateTimeOffset.UnixEpoch);
		sessionRegistry.TryAddGameSession(player);
		sessionRegistry.TryAddGameSession(other);
		var request = MakeRequest("us=cmyui-status&ha=correct-md5&m=1&mods=8"); // Taiko + Hidden, differs from defaults

		await _factory.CreateClient().SendAsync(request);

		Assert.NotEmpty(other.Dequeue());
	}

	[Fact]
	public async Task ModeAndModsUnchanged_DoesNotBroadcast()
	{
		var sessionRegistry = _factory.Services.GetRequiredService<IUserSessionRegistry>();
		var player = new GameSession(54, "cmyui-nochange", "tok4", UserPrivileges.Unrestricted,
			DateTimeOffset.UnixEpoch);
		var other = new GameSession(55, "other2", "other-token2", UserPrivileges.Unrestricted,
			DateTimeOffset.UnixEpoch);
		sessionRegistry.TryAddGameSession(player);
		sessionRegistry.TryAddGameSession(other);
		var request = MakeRequest("us=cmyui-nochange&ha=correct-md5&m=0&mods=0"); // matches PlayerStatus defaults

		await _factory.CreateClient().SendAsync(request);

		Assert.Empty(other.Dequeue());
	}
}