using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Shared.Settings;
using Basil.Domain.Beatmaps;
using Basil.Domain.Client;
using Basil.Domain.Cryptography;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;
using Basil.Gateway.Limits;
using Basil.Host.Stable.Tests.Support;
using Basil.Host.Stable.Web;
using Basil.Testing.Clients;
using Basil.Testing.Persistence;
using Microsoft.Extensions.DependencyInjection;
using OsuClient = Basil.Testing.Clients.OsuClient;

namespace Basil.Host.Stable.Tests.Web;

/// <summary>The osu! client's web endpoints, called the way the client calls them.</summary>
[Collection("Stable host")]
public sealed class WebEndpointTests(StableHostFixture fixture)
{
	private BasilHost Host => fixture.Host;

	private async Task<OsuClient> OnlineAsync(string name)
	{
		var client = await Host.NewClientAsync(name);
		await client.LoginAsync();
		return client;
	}

	[Fact]
	public async Task OverloadedServer_Answers503()
	{
		using var client = await OnlineAsync("WebShed");
		var load = Host.Services.GetRequiredService<LoadSignal>();
		load.Report(95, 100);

		try
		{
			var (status, _) = await client.GetFriendsAsync();

			Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
		}
		finally
		{
			load.Report(0, 100);
		}
	}

	[Fact]
	public async Task UnknownBeatmap_NotSubmitted()
	{
		using var client = await OnlineAsync("ScoresUnknown");

		var (status, text) = await client.GetScoresAsync("00000000000000000000000000000000");

		Assert.Equal(HttpStatusCode.OK, status);
		Assert.Equal("-1|false", text);
	}

	[Fact]
	public async Task ScorelessBeatmap_ListsHeaderOnly()
	{
		var map = (await Host.ImportSampleAsync("Sets/mosi-mosi-taiko.osz")).First();
		using var client = await OnlineAsync("ScoresEmpty");

		var (_, text) = await client.GetScoresAsync(map.Hash.ToString());

		var lines = text.Split('\n');
		Assert.Equal($"{(int)map.Beatmapset.Status}|false|{map.Id}|{map.Beatmapset.Id}|0|0", lines[0]);
		Assert.Equal("0", lines[1]);
	}

	[Fact]
	public async Task FilenameLookup_RepliesWithIds()
	{
		var map = (await Host.ImportSampleAsync("Sets/padoru-standard.osz")).First();
		using var client = await OnlineAsync("InfoByName");
		var filename = map.Metadata.GetFileName(map.Beatmapset.Metadata);

		var (status, text) = await client.GetBeatmapInfoAsync(filename);

		Assert.Equal(HttpStatusCode.OK, status);
		Assert.StartsWith($"0|{map.Id}|{map.Beatmapset.Id}|0|{(int)map.Beatmapset.Status}|", text);
		Assert.EndsWith(map.Hash.ToString(), text);
	}

	[Fact]
	public async Task CheatFlag_StopsClientOnly()
	{
		using var client = await OnlineAsync("LastFmUser");

		var (_, clean) = await client.LastFmAsync("a0");
		var (_, cheat) = await client.LastFmAsync($"a{(uint)OsuFlags.HqAssembly}");

		Assert.Equal(string.Empty, clean);
		Assert.Equal("-3", cheat);
	}

	[Fact]
	public async Task ValidPngScreenshot_ReturnsReference()
	{
		using var client = await OnlineAsync("ShotOk");

		var (status, text) = await client.UploadScreenshotAsync(Png.WithSize(16, 16));

		Assert.Equal(HttpStatusCode.OK, status);
		Assert.NotEmpty(text);
	}

	[Fact]
	public async Task HugeScreenshot_RefusedTooLarge()
	{
		using var client = await OnlineAsync("ShotHuge");

		var (status, _) = await client.UploadScreenshotAsync(Png.WithSize(8193, 1));

		Assert.Equal(HttpStatusCode.RequestEntityTooLarge, status);
	}

	[Fact]
	public async Task ScreenshotWithoutForm_BadRequest()
	{
		using var client = new HttpClient { BaseAddress = Host.Url };
		using var request = new HttpRequestMessage(HttpMethod.Post, "/web/osu-screenshot.php")
		{
			Content = new StringContent("{}", Encoding.UTF8, "application/json")
		};
		request.Headers.Host = $"osu.{Host.Domain}";
		using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}

	[Fact]
	public async Task RegisterWithoutForm_BadRequest()
	{
		using var client = new HttpClient { BaseAddress = Host.Url };
		using var request = new HttpRequestMessage(HttpMethod.Post, "/users")
		{
			Content = new StringContent("{}", Encoding.UTF8, "application/json")
		};
		request.Headers.Host = $"osu.{Host.Domain}";
		using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}

	[Fact]
	public async Task StoredScoreReplay_ServedAsUploaded()
	{
		var map = (await Host.ImportSampleAsync("Sets/padoru-standard.osz")).First();
		using var client = await OnlineAsync("ReplayOwner");
		var replay = Enumerable.Range(0, 64).Select(index => (byte)index).ToArray();
		await client.SubmitScoreAsync(map.Hash.ToString(), 5555, replay: replay);
		var (_, board) = await client.GetScoresAsync(map.Hash.ToString());
		var scoreId = board.Split('\n').Last(line => line.Contains("|ReplayOwner|")).Split('|')[0];

		var (status, _) = await client.WebGetAsync("/web/osu-getreplay.php", "u", "h", ("c", scoreId));

		Assert.Equal(HttpStatusCode.OK, status);
	}

	[Fact]
	public async Task WrongChecksum_Rejected()
	{
		var map = (await Host.ImportSampleAsync("Sets/padoru-standard.osz")).First();
		using var client = await OnlineAsync("BadChecksum");

		var (_, text) = await client.SubmitScoreAsync(map.Hash.ToString(), 100, checksumOverride: new string('0', 32));

		Assert.Equal("error: no", text);
	}

	[Fact]
	public async Task NewName_CreatesLoginAccount()
	{
		var (status, text) = await OsuClient.RegisterAsync(Host, "Newcomer", "hunter22", forwardedFor: "198.51.100.1");
		using var client = OsuClient.Create(Host, "Newcomer", "hunter22");
		await client.LoginAsync();

		Assert.Equal(HttpStatusCode.OK, status);
		Assert.Equal("ok", text);
		Assert.True(client.LoginCode > 0);
	}

	[Fact]
	public async Task SameAddressAgain_Refused()
	{
		await OsuClient.RegisterAsync(Host, "FirstOfIp", "hunter22", forwardedFor: "198.51.100.2");

		var (status, _) = await OsuClient.RegisterAsync(Host, "SecondOfIp", "hunter22", forwardedFor: "198.51.100.2");

		Assert.Equal(HttpStatusCode.TooManyRequests, status);
	}

	[Fact]
	public async Task AddressNameChecks_Limited()
	{
		HttpStatusCode last = default;
		for (var i = 0; i < OsuWebRoutes.RegistrationChecksPerWindow; i++)
			(last, _) = await OsuClient.RegisterAsync(Host, $"Probe{i}", "hunter22", true, "198.51.100.4");
		var (limited, _) = await OsuClient.RegisterAsync(Host, "ProbeExtra", "hunter22", true, "198.51.100.4");
		var (other, _) = await OsuClient.RegisterAsync(Host, "ProbeExtra", "hunter22", true, "198.51.100.5");

		Assert.Equal(HttpStatusCode.OK, last);
		Assert.Equal(HttpStatusCode.TooManyRequests, limited);
		Assert.Equal(HttpStatusCode.OK, other);
	}

	[Fact]
	public async Task TakenName_FormError()
	{
		await Host.CreateUserAsync("TakenName");

		var (status, text) = await OsuClient.RegisterAsync(Host, "TakenName", "hunter22", forwardedFor: "198.51.100.3");

		Assert.Equal(HttpStatusCode.BadRequest, status);
		Assert.Contains("form_error", text);
	}

	[Fact]
	public async Task DirectWithoutPrivilege_Forbidden()
	{
		var map = (await Host.ImportSampleAsync("Sets/padoru-standard.osz")).First();
		using var plain = await Host.NewClientAsync("DirectPlain",
			privileges: UserPrivilegesExtensions.Default & ~UserPrivileges.Supporter);
		await plain.LoginAsync();
		using var supporter = await Host.NewClientAsync("DirectSupport",
			privileges: UserPrivilegesExtensions.Default);
		await supporter.LoginAsync();

		var (denied, _) = await plain.WebGetAsync($"/d/{map.Beatmapset.Id}", "u", "h");
		var (allowed, _) = await supporter.WebGetAsync($"/d/{map.Beatmapset.Id}", "u", "h");
		var (searched, line) = await supporter.SearchSetAsync(map.Beatmapset.Id);

		Assert.Equal(HttpStatusCode.Forbidden, denied);
		Assert.Equal(HttpStatusCode.OK, allowed);
		Assert.Equal(HttpStatusCode.OK, searched);
		Assert.StartsWith($"{map.Beatmapset.Id}.osz|", line);
	}

	[Fact]
	public async Task HiddenSet_Unsearchable()
	{
		var map = (await Host.ImportSampleAsync("Sets/padoru-standard.osz")).First();
		var sets = Host.Services.GetRequiredService<IBeatmapsetRepository>();
		var set = (await sets.GetAsync(map.Beatmapset.Id, TestContext.Current.CancellationToken))!;
		set.Visible = false;
		await sets.UpdateAsync(set);
		using var supporter = await Host.NewClientAsync("DirectHiddenNo", privileges: UserPrivilegesExtensions.Default);
		await supporter.LoginAsync();

		try
		{
			var (download, _) = await supporter.WebGetAsync($"/d/{map.Beatmapset.Id}", "u", "h");
			var (_, line) = await supporter.SearchSetAsync(map.Beatmapset.Id);

			Assert.Equal(HttpStatusCode.NotFound, download);
			Assert.Equal(string.Empty, line);
		}
		finally
		{
			set.Visible = true;
			await sets.UpdateAsync(set);
		}
	}


	private async Task<(HttpStatusCode Status, byte[] Body)> OsuHostAsync(HttpMethod method, string path)
	{
		using var http = new HttpClient { BaseAddress = Host.Url };
		using var request = new HttpRequestMessage(method, path);
		request.Headers.Host = $"osu.{Host.Domain}";
		using var response = await http.SendAsync(request, TestContext.Current.CancellationToken);
		return (response.StatusCode, await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task MapFile_ServesTheOsuFile()
	{
		var map = (await Host.ImportSampleAsync("Sets/padoru-standard.osz")).First();
		var name = Uri.EscapeDataString(map.Metadata.GetFileName(map.Beatmapset.Metadata));

		var (status, body) = await OsuHostAsync(HttpMethod.Get, $"/web/maps/{name}");

		Assert.Equal(HttpStatusCode.OK, status);
		Assert.Equal(map.Hash.ToString(), Convert.ToHexString(MD5.HashData(body)).ToLowerInvariant());
	}

	[Fact]
	public async Task MapFile_UnknownName_NotFound()
	{
		var (status, _) = await OsuHostAsync(HttpMethod.Get, "/web/maps/Nobody%20-%20Nothing%20(Nobody)%20%5BNone%5D.osu");

		Assert.Equal(HttpStatusCode.NotFound, status);
	}

	[Fact]
	public async Task DifficultyRating_NoMods_AnswersStarsOfTheBeatmap()
	{
		var map = (await Host.ImportSampleAsync("Sets/padoru-standard.osz")).First();

		var (status, body) = await OsuHostAsync(HttpMethod.Post, $"/difficulty-rating?b={map.Id}&mods=0");

		using var json = JsonDocument.Parse(body);
		Assert.Equal(HttpStatusCode.OK, status);
		Assert.Equal(map.Id, json.RootElement.GetProperty("beatmap_id").GetInt32());
		Assert.Equal(0, json.RootElement.GetProperty("mods").GetInt32());
		Assert.Equal(map.Metadata.Difficulty.Star, json.RootElement.GetProperty("rating").GetDouble());
	}

	[Fact]
	public async Task DifficultyRating_DoubleTime_AnswersMoreStarsThanNoMod()
	{
		var map = (await Host.ImportSampleAsync("Sets/padoru-standard.osz")).First();

		var (status, body) = await OsuHostAsync(HttpMethod.Post, $"/difficulty-rating?b={map.Id}&mods=64");

		using var json = JsonDocument.Parse(body);
		Assert.Equal(HttpStatusCode.OK, status);
		Assert.Equal(64, json.RootElement.GetProperty("mods").GetInt32());
		Assert.True(json.RootElement.GetProperty("rating").GetDouble() > map.Metadata.Difficulty.Star);
	}

	[Theory]
	[InlineData("/difficulty-rating?b=abc&mods=0", HttpStatusCode.BadRequest)]
	[InlineData("/difficulty-rating?b=1&mods=", HttpStatusCode.BadRequest)]
	[InlineData("/difficulty-rating?b=2147483647&mods=0", HttpStatusCode.NotFound)]
	public async Task DifficultyRating_BadOrUnknown_Refused(string path, HttpStatusCode expected)
	{
		var (status, _) = await OsuHostAsync(HttpMethod.Post, path);

		Assert.Equal(expected, status);
	}

	[Fact]
	public async Task FavouritesRateComment_AnswerThePlayer()
	{
		using var client = await OnlineAsync("StubWebUser");

		var (listed, listing) = await client.WebGetAsync("/web/osu-getfavourites.php", "u", "h");
		var (added, addedText) = await client.WebGetAsync("/web/osu-addfavourite.php", "u", "h", ("a", "1"));
		var (rated, rating) = await client.WebGetAsync("/web/osu-rate.php", "u", "p", ("c", new string('0', 32)));
		var (commented, comments) = await client.WebPostFormAsync("/web/osu-comment.php", "u", "p", ("a", "get"));

		Assert.Equal((HttpStatusCode.OK, string.Empty), (listed, listing));
		Assert.Equal((HttpStatusCode.OK, string.Empty), (added, addedText));
		Assert.Equal((HttpStatusCode.OK, OsuWebReplies.NotRanked), (rated, rating));
		Assert.Equal((HttpStatusCode.OK, string.Empty), (commented, comments));
	}

	[Fact]
	public async Task FavouritesRateComment_WrongPassword_Unauthorized()
	{
		using var client = await OnlineAsync("StubWebWrong");
		using var impostor = OsuClient.Create(Host, "StubWebWrong", "wrong");

		var (listed, _) = await impostor.WebGetAsync("/web/osu-getfavourites.php", "u", "h", ("x", "1"));
		var (added, _) = await impostor.WebGetAsync("/web/osu-addfavourite.php", "u", "h", ("a", "1"));
		var (rated, _) = await impostor.WebGetAsync("/web/osu-rate.php", "u", "p", ("c", new string('0', 32)));
		var (commented, _) = await impostor.WebPostFormAsync("/web/osu-comment.php", "u", "p", ("a", "get"));

		Assert.All([listed, added, rated, commented], status => Assert.Equal(HttpStatusCode.Unauthorized, status));
	}

	private async Task<OsuClient> SupporterAsync(string name)
	{
		var client = await Host.NewClientAsync(name, privileges: UserPrivilegesExtensions.Default);
		await client.LoginAsync();
		return client;
	}

	private static Task<(HttpStatusCode Status, string Text)> SearchAsync(OsuClient client, string q, int r = 4,
		int m = -1, int p = 0)
	{
		return client.WebGetAsync("/web/osu-search.php", "u", "h", ("q", q), ("r", r.ToString()), ("m", m.ToString()),
			("p", p.ToString()));
	}

	private Task StoreSetAsync(int id, string artist, string title, params BeatmapData[] difficulties)
	{
		return Host.Services.GetRequiredService<IBeatmapsetRepository>().CommitAsync(new BeatmapsetChanges(
			[new BeatmapsetChange(null, StoredBeatmaps.Data(id, artist, title, "Mapper", difficulties))], []));
	}

	[Fact]
	public async Task Search_WritesSetLineWithDifficultiesByStars()
	{
		await StoreSetAsync(700_001, "Art|ist", "Zqlines Alpha",
			StoredBeatmaps.Difficulty(null, new Md5("a0000000000000000000000000000001"), "Hard|Er", cs: 4, ar: 9.3,
				od: 8, hp: 5, stars: 5.5),
			StoredBeatmaps.Difficulty(null, new Md5("a0000000000000000000000000000002"), "Easy", cs: 3.5, ar: 5, od: 4,
				hp: 3, stars: 1.234));
		using var client = await SupporterAsync("SearchLines");

		var (status, text) = await SearchAsync(client, "Zqlines");

		var lines = text.Split('\n');
		Assert.Equal(HttpStatusCode.OK, status);
		Assert.Equal("1", lines[0]);
		Assert.StartsWith($"700001.osz|ArtIist|Zqlines Alpha|Mapper|{(int)BeatmapsetStatus.Ranked}|10.0|", lines[1]);
		Assert.EndsWith("|700001|0|0|0|0|0|[1.23\u2b50] Easy {cs: 3.5 / od: 4.0 / ar: 5.0 / hp: 3.0}@0," +
		                "[5.50\u2b50] HardIEr {cs: 4.0 / od: 8.0 / ar: 9.3 / hp: 5.0}@0", lines[1]);
	}

	[Fact]
	public async Task Search_FiltersByModeAndStatus()
	{
		await StoreSetAsync(700_002, "Zqfilter", "Taiko Only",
			StoredBeatmaps.Difficulty(null, new Md5("a0000000000000000000000000000003"), "Oni", GameMode.Taiko));
		using var client = await SupporterAsync("SearchFilter");

		var (_, taiko) = await SearchAsync(client, "Zqfilter", m: 1);
		var (_, standard) = await SearchAsync(client, "Zqfilter", m: 0);
		var (_, everyMode) = await SearchAsync(client, "Zqfilter", m: -1);
		var (_, ranked) = await SearchAsync(client, "Zqfilter", r: 0);
		var (_, loved) = await SearchAsync(client, "Zqfilter", r: 8);

		Assert.Equal("1", taiko.Split('\n')[0]);
		Assert.Equal("0", standard);
		Assert.Equal("1", everyMode.Split('\n')[0]);
		Assert.Equal("1", ranked.Split('\n')[0]);
		Assert.Equal("0", loved);
	}

	[Fact]
	public async Task Search_FullPage_ReportsMoreAndPagesByHundred()
	{
		await Host.Services.GetRequiredService<IBeatmapsetRepository>().CommitAsync(new BeatmapsetChanges(
			Enumerable.Range(1, 100).Select(i => new BeatmapsetChange(null, StoredBeatmaps.Data(710_000 + i, "Zqpage",
				$"Set {i}", "Mapper",
				[StoredBeatmaps.Difficulty(null, new Md5(i.ToString("x32")), "Normal")]))).ToList(), []));
		using var client = await SupporterAsync("SearchPaging");

		var (_, first) = await SearchAsync(client, "Zqpage");
		var (_, second) = await SearchAsync(client, "Zqpage", p: 1);

		var lines = first.Split('\n');
		Assert.Equal("101", lines[0]);
		Assert.Equal(101, lines.Length);
		Assert.Equal("0", second);
	}

	[Theory]
	[InlineData("Newest")]
	[InlineData("Top Rated")]
	[InlineData("Most Played")]
	public async Task Search_SortTab_FiltersNothing(string tab)
	{
		await StoreSetAsync(700_100 + tab.Length, "Zqtab", "Any", StoredBeatmaps.Difficulty(null,
			new Md5("b" + tab.Length.ToString("x31")), "Normal"));
		using var client = await SupporterAsync("SearchTab" + tab.Length);

		var (_, text) = await SearchAsync(client, tab);

		Assert.NotEqual("0", text.Split('\n')[0]);
	}

	[Fact]
	public async Task Search_MissingOrInvalidParameter_BadRequest()
	{
		using var client = await SupporterAsync("SearchBad");

		var (missing, _) = await client.WebGetAsync("/web/osu-search.php", "u", "h", ("q", "x"));
		var (badStatus, _) = await SearchAsync(client, "x", r: 9);
		var (badMode, _) = await SearchAsync(client, "x", m: 4);
		var (badPage, _) = await SearchAsync(client, "x", p: -1);

		Assert.All([missing, badStatus, badMode, badPage], status => Assert.Equal(HttpStatusCode.BadRequest, status));
	}

	[Fact]
	public async Task Search_WithoutPrivilege_Forbidden()
	{
		using var plain = await Host.NewClientAsync("SearchPlain",
			privileges: UserPrivilegesExtensions.Default & ~UserPrivileges.Supporter);
		await plain.LoginAsync();

		var (status, _) = await SearchAsync(plain, "x");

		Assert.Equal(HttpStatusCode.Forbidden, status);
	}

	private async Task<int> LoginLimitAsync()
	{
		return (await Host.Services.GetRequiredService<ISettingsRepository>().GetAsync()).RateLimits.Login.MaxAttempts;
	}

	[Fact]
	public async Task HashBeforeLimit_Succeeds()
	{
		using var client = await OnlineAsync("WebRateOk");
		using var impostor = OsuClient.Create(Host, "WebRateOk", "wrong");
		await impostor.GetFriendsAsync("203.0.113.30");

		var (status, _) = await client.GetFriendsAsync("203.0.113.30");

		Assert.Equal(HttpStatusCode.OK, status);
	}

	[Fact]
	public async Task HashAfterCorrect_Refused()
	{
		using var client = await OnlineAsync("WebRepeat");
		using var impostor = OsuClient.Create(Host, "WebRepeat", "wrong");
		await client.GetFriendsAsync("203.0.113.40");

		var (wrong, _) = await impostor.GetFriendsAsync("203.0.113.41");
		var (again, _) = await client.GetFriendsAsync("203.0.113.40");

		Assert.Equal(HttpStatusCode.Unauthorized, wrong);
		Assert.Equal(HttpStatusCode.OK, again);
	}

	[Fact]
	public async Task ManyWrongHashes_RefusesCorrect()
	{
		using var client = await OnlineAsync("WebRateLocked");
		using var impostor = OsuClient.Create(Host, "WebRateLocked", "wrong");
		for (var attempt = 0; attempt < await LoginLimitAsync(); attempt++)
			await impostor.GetFriendsAsync("203.0.113.31");

		var (status, _) = await client.GetFriendsAsync("203.0.113.31");

		Assert.Equal(HttpStatusCode.Unauthorized, status);
	}

	[Fact]
	public async Task WindowPassed_AcceptsCorrectHash()
	{
		using var client = await OnlineAsync("WebRateWindow");
		using var impostor = OsuClient.Create(Host, "WebRateWindow", "wrong");
		for (var attempt = 0; attempt < await LoginLimitAsync(); attempt++)
			await impostor.GetFriendsAsync("203.0.113.32");
		var window =
			(await Host.Services.GetRequiredService<ISettingsRepository>()
				.GetAsync(TestContext.Current.CancellationToken)).RateLimits.Login.Window;

		// advance in steps shorter than the idle limit, polling so the client stays online
		for (var elapsed = TimeSpan.Zero; elapsed <= window; elapsed += TimeSpan.FromMinutes(2))
		{
			Host.Time.Advance(TimeSpan.FromMinutes(2));
			await client.PollAsync();
		}

		var (status, _) = await client.GetFriendsAsync("203.0.113.32");

		Assert.Equal(HttpStatusCode.OK, status);
	}

	[Fact]
	public async Task ConcurrentWrongHashes_StayWithinLimit()
	{
		using var client = await OnlineAsync("WebRateParallel");
		using var impostor = OsuClient.Create(Host, "WebRateParallel", "wrong");
		var limit = await LoginLimitAsync();

		// Every request reaches the check at once; the check must still stop at the limit.
		await Task.WhenAll(Enumerable.Range(0, limit * 3)
			.Select(_ => impostor.GetFriendsAsync("203.0.113.35")));
		var (status, _) = await client.GetFriendsAsync("203.0.113.35");

		Assert.Equal(HttpStatusCode.Unauthorized, status);
	}

	[Fact]
	public async Task AccountWrongHashes_OthersFree()
	{
		using var victim = await OnlineAsync("WebRateVictim");
		using var other = await OnlineAsync("WebRateOther");
		using var impostor = OsuClient.Create(Host, "WebRateVictim", "wrong");
		for (var attempt = 0; attempt < await LoginLimitAsync(); attempt++)
			await impostor.GetFriendsAsync("203.0.113.33");

		var (status, _) = await other.GetFriendsAsync("203.0.113.34");

		Assert.Equal(HttpStatusCode.OK, status);
	}
}