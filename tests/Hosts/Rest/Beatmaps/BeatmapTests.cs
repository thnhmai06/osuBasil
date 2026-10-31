using System.Net.Http.Headers;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Storage;
using Basil.Domain.Users;
using Basil.Testing.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Rest.Tests.Beatmaps;

/// <summary>Beatmapsets and beatmaps: public reads, hidden sets, upload, management and the redirect rule.</summary>
[Collection("REST host")]
public sealed class BeatmapTests(RestHostFixture fixture)
{
	private static readonly UserPermissions Manager = UserPermissionsGroups.Default | UserPermissions.BeatmapsetManage;

	private async Task<(int SetId, int BeatmapId)> ImportAsync(string sample)
	{
		var beatmaps = await fixture.Host.ImportSampleAsync(sample);
		var first = beatmaps.OrderBy(b => b.Id).First();
		return (first.Beatmapset.Id, first.Id);
	}

	[Fact]
	public async Task Get_ASetAndABeatmap_IsPublic_AndTheBeatmapCarriesItsSet()
	{
		var (setId, mapId) = await ImportAsync("Sets/padoru-standard.osz");
		using var client = new RestClient(fixture.Host.Url, fixture.Host.Domain);

		var (setStatus, set) = await client.CallAsync(HttpMethod.Get, $"/v1/beatmapsets/{setId}");
		var (mapStatus, map) = await client.CallAsync(HttpMethod.Get, $"/v1/beatmaps/{mapId}");

		Assert.Equal(200, setStatus);
		Assert.NotEmpty(set!["data"]!["beatmaps"]!.AsArray());
		Assert.Equal(200, mapStatus);
		Assert.Equal(setId, (int)map!["data"]!["set"]!["id"]!);
	}

	[Fact]
	public async Task Get_UnknownSet_Answers404()
	{
		using var client = new RestClient(fixture.Host.Url, fixture.Host.Domain);

		var (status, _) = await client.CallAsync(HttpMethod.Get, "/v1/beatmapsets/999999999");

		Assert.Equal(404, status);
	}

	[Fact]
	public async Task Search_NeedsAToken_AndPagingIsBounded()
	{
		var (client, _) = await fixture.SignInAsync("BmSearcher");
		using (client)
		using (var anon = new RestClient(fixture.Host.Url, fixture.Host.Domain))
		{
			var (denied, _) = await anon.CallAsync(HttpMethod.Get, "/v1/beatmapsets");
			var (ok, _) = await client.CallAsync(HttpMethod.Get, "/v1/beatmapsets?limit=100");
			var (tooMany, _) = await client.CallAsync(HttpMethod.Get, "/v1/beatmapsets?limit=101");

			Assert.Equal(401, denied);
			Assert.Equal(200, ok);
			Assert.Equal(422, tooMany);
		}
	}

	[Fact]
	public async Task Patch_ByAManager_HidesTheSetFromEveryoneButManagers()
	{
		var (setId, _) = await ImportAsync("Sets/mosi-mosi-taiko.osz");
		var (manager, _) = await fixture.SignInAsync("BmManager", Manager);
		var (plain, _) = await fixture.SignInAsync("BmPlain");
		using (manager)
		using (plain)
		using (var anon = new RestClient(fixture.Host.Url, fixture.Host.Domain))
		{
			var (denied, _) = await plain.CallAsync(HttpMethod.Patch, $"/v1/beatmapsets/{setId}", new { visible = false });
			var (patched, _) = await manager.CallAsync(HttpMethod.Patch, $"/v1/beatmapsets/{setId}", new { visible = false });
			var (hiddenForAnon, _) = await anon.CallAsync(HttpMethod.Get, $"/v1/beatmapsets/{setId}");
			var (hiddenForPlain, _) = await plain.CallAsync(HttpMethod.Get, $"/v1/beatmapsets/{setId}");
			var (visibleForManager, _) = await manager.CallAsync(HttpMethod.Get, $"/v1/beatmapsets/{setId}");
			await manager.CallAsync(HttpMethod.Patch, $"/v1/beatmapsets/{setId}", new { visible = true });

			Assert.Equal(403, denied);
			Assert.Equal(200, patched);
			Assert.Equal(404, hiddenForAnon);
			Assert.Equal(404, hiddenForPlain);
			Assert.Equal(200, visibleForManager);
		}
	}

	[Fact]
	public async Task Patch_Status_IsStoredAndShown()
	{
		var (setId, _) = await ImportAsync("Sets/selene-ui-mania.osz");
		var (manager, _) = await fixture.SignInAsync("BmStatus", Manager);
		using (manager)
		{
			var (patched, _) = await manager.CallAsync(HttpMethod.Patch, $"/v1/beatmapsets/{setId}", new { status = "loved" });
			var (_, set) = await manager.CallAsync(HttpMethod.Get, $"/v1/beatmapsets/{setId}");

			Assert.Equal(200, patched);
			Assert.Equal("loved", (string)set!["data"]!["set"]!["status"]!);
		}
	}

	[Fact]
	public async Task Upload_Again_Answers409_GarbageAnswers422_AndDeleteIsForManagersOnly()
	{
		var (client, _) = await fixture.SignInAsync("BmUploader");
		var (manager, _) = await fixture.SignInAsync("BmDeleter", Manager);
		using (client)
		using (manager)
		{
			var archive = OszArchive.Sample("Sets/hopes-and-dreams-catch.osz");

			var (created, envelope) = await UploadAsync(manager, archive);
			var (again, duplicate) = await UploadAsync(manager, archive);
			var (garbage, _) = await UploadAsync(manager, [1, 2, 3, 4]);
			var setId = (int)envelope!["data"]!["set"]!["id"]!;
			var (denied, _) = await client.CallAsync(HttpMethod.Delete, $"/v1/beatmapsets/{setId}");
			var (deleted, _) = await manager.CallAsync(HttpMethod.Delete, $"/v1/beatmapsets/{setId}");
			var (gone, _) = await manager.CallAsync(HttpMethod.Get, $"/v1/beatmapsets/{setId}");

			Assert.Equal(201, created);
			Assert.Equal(409, again);
			Assert.Equal("alreadyExists", (string)duplicate!["errors"]!["code"]!);
			Assert.Equal(422, garbage);
			Assert.Equal(403, denied);
			Assert.Equal(200, deleted);
			Assert.Equal(404, gone);
		}
	}

	[Fact]
	public async Task Upload_ByAPlainUser_WhileTheAreaIsLockedByDefault_Answers409Locked()
	{
		var (client, _) = await fixture.SignInAsync("BmLocked");
		using (client)
		{
			var (status, envelope) = await UploadAsync(client, OszArchive.Sample("Sets/hopes-and-dreams-catch.osz"));

			Assert.Equal(409, status);
			Assert.Equal("locked", (string)envelope!["errors"]!["code"]!);
		}
	}

	[Fact]
	public async Task Upload_WithoutAToken_Answers401()
	{
		using var client = new RestClient(fixture.Host.Url, fixture.Host.Domain);

		var (status, _) = await UploadAsync(client, [1]);

		Assert.Equal(401, status);
	}

	[Fact]
	public async Task SetLevelResources_RedirectToTheLowestBeatmap_AndTheTokenRulesHold()
	{
		var (setId, mapId) = await ImportAsync("Sets/padoru-standard.osz");
		var (client, _) = await fixture.SignInAsync("BmRedirect");
		using (client)
		using (var anon = new RestClient(fixture.Host.Url, fixture.Host.Domain))
		{
			using var audio = await client.GetAsync($"/v1/beatmapsets/{setId}/audio");
			using var audioAnon = await anon.GetAsync($"/v1/beatmapsets/{setId}/audio");
			using var background = await anon.GetAsync($"/v1/beatmapsets/{setId}/background");
			using var download = await anon.GetAsync($"/v1/beatmapsets/{setId}/download");

			Assert.Equal(307, (int)audio.StatusCode);
			Assert.Equal($"/v1/beatmaps/{mapId}/audio", audio.Headers.Location!.OriginalString);
			Assert.Equal(401, (int)audioAnon.StatusCode);
			Assert.Equal(307, (int)background.StatusCode);
			Assert.Equal(401, (int)download.StatusCode);
		}
	}

	[Fact]
	public async Task AHiddenBeatmap_IsNotFoundForEveryoneButViewersOfHiddenContent_AndNeverTheRedirectTarget()
	{
		var beatmaps = await fixture.Host.ImportSampleAsync("Sets/selene-ui-mania.osz");
		var hidden = beatmaps.OrderBy(b => b.Id).First();
		var repository = fixture.Host.Services.GetRequiredService<IBeatmapRepository>();
		var viewer = await fixture.SignInAsync("BmHiddenViewer", UserPermissionsGroups.Default | UserPermissions.BeatmapsetManageViewHidden);
		hidden.Visible = false;
		await repository.UpdateAsync(hidden);
		try
		{
			using (viewer.Client)
			using (var anon = new RestClient(fixture.Host.Url, fixture.Host.Domain))
			{
				var (anonymous, _) = await anon.CallAsync(HttpMethod.Get, $"/v1/beatmaps/{hidden.Id}");
				var (permitted, _) = await viewer.Client.CallAsync(HttpMethod.Get, $"/v1/beatmaps/{hidden.Id}");
				var (anonymousFile, _) = await anon.CallAsync(HttpMethod.Get, $"/v1/beatmaps/{hidden.Id}/background");
				var (setForAnon, set) = await anon.CallAsync(HttpMethod.Get, $"/v1/beatmapsets/{hidden.Beatmapset.Id}");
				var (_, setForViewer) = await viewer.Client.CallAsync(HttpMethod.Get, $"/v1/beatmapsets/{hidden.Beatmapset.Id}");
				using var redirect = await anon.GetAsync($"/v1/beatmapsets/{hidden.Beatmapset.Id}/background");

				Assert.Equal(404, anonymous);
				Assert.Equal(200, permitted);
				Assert.Equal(404, anonymousFile);
				Assert.Equal(200, setForAnon);
				Assert.DoesNotContain(set!["data"]!["beatmaps"]!.AsArray(), b => (int)b!["id"]! == hidden.Id);
				Assert.Contains(setForViewer!["data"]!["beatmaps"]!.AsArray(), b => (int)b!["id"]! == hidden.Id);
				Assert.DoesNotContain($"/v1/beatmaps/{hidden.Id}/", redirect.Headers.Location?.OriginalString ?? "");
			}
		}
		finally
		{
			hidden.Visible = true;
			await repository.UpdateAsync(hidden);
		}
	}

	[Theory]
	[InlineData("pic.png", "image/png")]
	[InlineData("pic.JPG", "image/jpeg")]
	[InlineData("evil.html", "application/octet-stream")]
	[InlineData("evil.svg", "application/octet-stream")]
	[InlineData("evil.png.html", "application/octet-stream")]
	public async Task Background_IsServedWithATypeFromTheAllowlist_AndNoSniff(string fileName, string expected)
	{
		var beatmaps = await fixture.Host.ImportSampleAsync("Sets/mosi-mosi-taiko.osz");
		var beatmap = beatmaps.OrderBy(b => b.Id).First();
		beatmap.Assets = beatmap.Assets with { BackgroundFile = fileName };
		await fixture.Host.Services.GetRequiredService<IBeatmapRepository>().UpdateAsync(beatmap);
		await fixture.Host.Services.GetRequiredService<IBeatmapsetStorage>().SaveAsync(beatmap.Beatmapset,
			BeatmapsetFileType.Background, fileName, new MemoryStream("<script>alert(1)</script>"u8.ToArray()));
		using var anon = new RestClient(fixture.Host.Url, fixture.Host.Domain);

		using var response = await anon.GetAsync($"/v1/beatmaps/{beatmap.Id}/background");

		Assert.Equal(200, (int)response.StatusCode);
		Assert.Equal(expected, response.Content.Headers.ContentType!.MediaType);
		Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
	}

	[Fact]
	public async Task Download_WithAToken_ReturnsAnArchive()
	{
		var (setId, _) = await ImportAsync("Sets/padoru-standard.osz");
		var (client, _) = await fixture.SignInAsync("BmDownload");
		using (client)
		{
			using var response = await client.GetAsync($"/v1/beatmapsets/{setId}/download");

			Assert.Equal(200, (int)response.StatusCode);
			Assert.Equal("application/x-osu-beatmap-archive", response.Content.Headers.ContentType!.MediaType);
		}
	}

	private async Task<(int Status, System.Text.Json.Nodes.JsonNode? Envelope)> UploadAsync(RestClient client, byte[] archive)
	{
		using var form = new MultipartFormDataContent();
		var file = new ByteArrayContent(archive);
		file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
		form.Add(file, "file", "set.osz");
		using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/beatmapsets") { Content = form };
		request.Headers.Host = $"api.{fixture.Host.Domain}";
		if (client.Bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", client.Bearer);
		using var http = new HttpClient { BaseAddress = fixture.Host.Url };
		using var response = await http.SendAsync(request);
		var text = await response.Content.ReadAsStringAsync();
		return ((int)response.StatusCode, text.Length == 0 ? null : System.Text.Json.Nodes.JsonNode.Parse(text));
	}
}
