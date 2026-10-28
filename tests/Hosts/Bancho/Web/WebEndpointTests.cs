using System.Net;
using Basil.Domain.Bancho;
using Basil.Host.Bancho.Tests.Support;
using Basil.Testing.Clients;

namespace Basil.Host.Bancho.Tests.Web;

/// <summary>The osu! client's web endpoints, called the way the client calls them.</summary>
[Collection("Bancho host")]
public sealed class WebEndpointTests(BanchoHostFixture fixture)
{
	private BasilHost Host => fixture.Host;

	private async Task<OsuClient> OnlineAsync(string name)
	{
		var client = await Host.NewClientAsync(name);
		await client.LoginAsync();
		return client;
	}

	[Fact]
	public async Task GetScores_UnknownBeatmap_AnswersNotSubmitted()
	{
		using var client = await OnlineAsync("ScoresUnknown");

		var (status, text) = await client.GetScoresAsync("00000000000000000000000000000000");

		Assert.Equal(HttpStatusCode.OK, status);
		Assert.Equal("-1|false", text);
	}

	[Fact]
	public async Task GetScores_KnownBeatmapWithoutScores_ListsTheHeaderOnly()
	{
		var map = (await Host.ImportSampleAsync("Sets/mosi-mosi-taiko.osz")).First();
		using var client = await OnlineAsync("ScoresEmpty");

		var (_, text) = await client.GetScoresAsync(map.Hash.ToString());

		var lines = text.Split('\n');
		Assert.Equal($"{(int)map.Beatmapset.Status}|false|{map.Id}|{map.Beatmapset.Id}|0|0", lines[0]);
		Assert.Equal("0", lines[1]);
	}

	[Fact]
	public async Task GetBeatmapInfo_ByFilename_RepliesWithTheBeatmapIds()
	{
		var map = (await Host.ImportSampleAsync("Sets/padoru-standard.osz")).First();
		using var client = await OnlineAsync("InfoByName");
		var filename = map.Metadata.OsuFileName(map.Beatmapset.Metadata);

		var (status, text) = await client.GetBeatmapInfoAsync(filename);

		Assert.Equal(HttpStatusCode.OK, status);
		Assert.StartsWith($"0|{map.Id}|{map.Beatmapset.Id}|0|{(int)map.Beatmapset.Status}|", text);
		Assert.EndsWith(map.Hash.ToString(), text);
	}

	[Fact]
	public async Task LastFm_CheatFlag_TellsTheClientToStop_AndCleanFlagsDoNot()
	{
		using var client = await OnlineAsync("LastFmUser");

		var (_, clean) = await client.LastFmAsync("a0");
		var (_, cheat) = await client.LastFmAsync($"a{(uint)BanchoFlags.HqAssembly}");

		Assert.Equal(string.Empty, clean);
		Assert.Equal("-3", cheat);
	}

	[Fact]
	public async Task Screenshot_ValidPng_ReturnsItsReference()
	{
		using var client = await OnlineAsync("ShotOk");

		var (status, text) = await client.UploadScreenshotAsync(Png.WithSize(16, 16));

		Assert.Equal(HttpStatusCode.OK, status);
		Assert.NotEmpty(text);
	}

	[Fact]
	public async Task Screenshot_TooManyPixelsPerSide_IsRefusedAsTooLarge()
	{
		using var client = await OnlineAsync("ShotHuge");

		var (status, _) = await client.UploadScreenshotAsync(Png.WithSize(8193, 1));

		Assert.Equal(HttpStatusCode.RequestEntityTooLarge, status);
	}

	[Fact]
	public async Task Replay_OfAStoredScore_IsServedAsTheUploadedBytes()
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
	public async Task SubmitScore_WithWrongChecksum_IsRejected()
	{
		var map = (await Host.ImportSampleAsync("Sets/padoru-standard.osz")).First();
		using var client = await OnlineAsync("BadChecksum");

		var (_, text) = await client.SubmitScoreAsync(map.Hash.ToString(), 100, checksumOverride: new string('0', 32));

		Assert.Equal("error: no", text);
	}

	[Fact]
	public async Task Register_NewName_CreatesAnAccountThatCanLogIn()
	{
		var (status, text) = await OsuClient.RegisterAsync(Host, "Newcomer", "hunter22", forwardedFor: "198.51.100.1");
		using var client = OsuClient.Create(Host, "Newcomer", "hunter22");
		await client.LoginAsync();

		Assert.Equal(HttpStatusCode.OK, status);
		Assert.Equal("ok", text);
		Assert.True(client.LoginCode > 0);
	}

	[Fact]
	public async Task Register_SecondAccountFromTheSameAddressWithinADay_IsRefused()
	{
		await OsuClient.RegisterAsync(Host, "FirstOfIp", "hunter22", forwardedFor: "198.51.100.2");

		var (status, _) = await OsuClient.RegisterAsync(Host, "SecondOfIp", "hunter22", forwardedFor: "198.51.100.2");

		Assert.Equal(HttpStatusCode.TooManyRequests, status);
	}

	[Fact]
	public async Task Register_TakenName_IsRefusedWithAFormError()
	{
		await Host.CreateUserAsync("TakenName");

		var (status, text) = await OsuClient.RegisterAsync(Host, "TakenName", "hunter22", forwardedFor: "198.51.100.3");

		Assert.Equal(HttpStatusCode.BadRequest, status);
		Assert.Contains("form_error", text);
	}

	[Fact]
	public async Task Direct_WithoutThePermission_IsForbidden_AndWithItServesTheArchive()
	{
		var map = (await Host.ImportSampleAsync("Sets/padoru-standard.osz")).First();
		using var plain = await Host.NewClientAsync("DirectPlain",
			permissions: Basil.Domain.Users.UserPermissionsGroups.Default & ~Basil.Domain.Users.UserPermissions.BeatmapsetDirect);
		await plain.LoginAsync();
		using var supporter = await Host.NewClientAsync("DirectSupport",
			permissions: Basil.Domain.Users.UserPermissionsGroups.Default);
		await supporter.LoginAsync();

		var (denied, _) = await plain.WebGetAsync($"/d/{map.Beatmapset.Id}", "u", "h");
		var (allowed, _) = await supporter.WebGetAsync($"/d/{map.Beatmapset.Id}", "u", "h");
		var (searched, line) = await supporter.SearchSetAsync(map.Beatmapset.Id);

		Assert.Equal(HttpStatusCode.Forbidden, denied);
		Assert.Equal(HttpStatusCode.OK, allowed);
		Assert.Equal(HttpStatusCode.OK, searched);
		Assert.StartsWith($"{map.Beatmapset.Id}.osz|", line);
	}
}
