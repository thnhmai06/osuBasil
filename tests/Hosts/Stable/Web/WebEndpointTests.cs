using System.Net;
using System.Text;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Common.Settings;
using Basil.Domain.Client;
using Basil.Domain.Users;
using Basil.Gateway;
using Basil.Host.Stable.Tests.Support;
using Basil.Host.Stable.Web;
using Basil.Testing.Clients;
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
		var window = (await Host.Services.GetRequiredService<ISettingsRepository>().GetAsync(TestContext.Current.CancellationToken)).RateLimits.Login.Window;

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