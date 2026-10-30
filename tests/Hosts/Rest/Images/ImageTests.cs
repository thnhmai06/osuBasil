using System.Net.Http.Headers;
using System.Text;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Content.Persistence;
using Basil.Domain.Users;
using Basil.Testing.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Rest.Tests.Images;

/// <summary>The image hosts: public, through one pipeline, with HTTP caching and the visibility rules inside.</summary>
[Collection("REST host")]
public sealed class ImageTests(RestHostFixture fixture)
{
	private static readonly byte[] Png = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "Images", "avatar-default.png"));

	private RestClient Anonymous() => new(fixture.Host.Url, fixture.Host.Domain);

	[Fact]
	public async Task Avatar_OfAUserWithoutOne_IsTheDefault_AndBasilBotsIsItsOwn()
	{
		using var client = Anonymous();

		using var plain = await client.GetFromAsync("a", "/987654");
		using var bot = await client.GetFromAsync("a", "/1");

		Assert.Equal(200, (int)plain.StatusCode);
		Assert.Equal("image/png", plain.Content.Headers.ContentType!.MediaType);
		Assert.Equal(200, (int)bot.StatusCode);
		Assert.NotEqual(await plain.Content.ReadAsByteArrayAsync(), await bot.Content.ReadAsByteArrayAsync());
	}

	[Fact]
	public async Task Avatar_Uploaded_IsServedWithCachingHeaders_AndAnUnchangedEtagAnswers304()
	{
		var (owner, id) = await fixture.SignInAsync("ImgAvatar");
		using (owner)
		using (var anon = Anonymous())
		{
			var status = await PutFileAsync(owner, $"/v1/users/{id}/avatar", Png);
			using var first = await anon.GetFromAsync("a", $"/{id}");
			var etag = first.Headers.ETag?.Tag;
			using var second = await anon.GetFromAsync("a", $"/{id}", etag);

			Assert.Equal(200, status);
			Assert.Equal(200, (int)first.StatusCode);
			Assert.False(string.IsNullOrEmpty(etag));
			Assert.Equal("public,max-age=300", first.Headers.CacheControl?.ToString().Replace(" ", ""));
			Assert.Equal(304, (int)second.StatusCode);
		}
	}

	[Fact]
	public async Task Avatar_WhosePictureCannotBeDecoded_IsNotFound()
	{
		var headerOnly = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "Images", "seasonal-3500x2487-header.png"));
		var (owner, id) = await fixture.SignInAsync("ImgBrokenAvatar");
		using (owner)
		using (var anon = Anonymous())
		{
			var status = await PutFileAsync(owner, $"/v1/users/{id}/avatar", headerOnly);
			using var served = await anon.GetFromAsync("a", $"/{id}");

			Assert.Equal(200, status);
			Assert.Equal(404, (int)served.StatusCode);
		}
	}

	[Fact]
	public async Task Avatar_CanBeResizedByQuery()
	{
		var (owner, id) = await fixture.SignInAsync("ImgResize");
		using (owner)
		using (var anon = Anonymous())
		{
			await PutFileAsync(owner, $"/v1/users/{id}/avatar", Png);

			using var small = await anon.GetFromAsync("a", $"/{id}?width=8&height=8");
			using var full = await anon.GetFromAsync("a", $"/{id}");

			Assert.Equal(200, (int)small.StatusCode);
			Assert.True((await small.Content.ReadAsByteArrayAsync()).Length < (await full.Content.ReadAsByteArrayAsync()).Length);
		}
	}

	[Fact]
	public async Task Avatar_TooLarge_Answers422()
	{
		var (owner, id) = await fixture.SignInAsync("ImgBigAvatar");
		using (owner)
		{
			var big = new byte[6 * 1024 * 1024];

			var status = await PutFileAsync(owner, $"/v1/users/{id}/avatar", big);

			Assert.Equal(422, status);
		}
	}

	[Fact]
	public async Task Avatar_ThatIsNotAPicture_Answers422()
	{
		var (owner, id) = await fixture.SignInAsync("ImgNotPicture");
		using (owner)
		{
			var html = Encoding.UTF8.GetBytes("<!doctype html><script>alert(1)</script>");

			var status = await PutFileAsync(owner, $"/v1/users/{id}/avatar", html);

			Assert.Equal(422, status);
		}
	}

	[Fact]
	public async Task Screenshot_IsPublicOnTheOsuHost()
	{
		var reference = await fixture.Host.Services.GetRequiredService<IScreenshotStorage>()
			.SaveAsync(new MemoryStream(Png), "png");
		using var client = Anonymous();

		using var response = await client.GetFromAsync("osu", "/ss/" + Path.GetFileName(reference.Value));
		using var missing = await client.GetFromAsync("osu", "/ss/00000000-0000-0000-0000-000000000000.png");

		Assert.Equal(200, (int)response.StatusCode);
		Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
		Assert.Equal(404, (int)missing.StatusCode);
	}

	[Fact]
	public async Task MenuBanner_IsServedOnTheAssetsHost()
	{
		await fixture.Host.Services.GetRequiredService<IMenuBannerStorage>().SaveAsync("rest-banner.png", new MemoryStream(Png));
		using var client = Anonymous();

		using var response = await client.GetFromAsync("assets", "/menu/banners/rest-banner.png");

		Assert.Equal(200, (int)response.StatusCode);
		Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
	}

	[Fact]
	public async Task BeatmapCover_IsPublic_AHiddenSetShowsNothing_AndTheThumbnailHasTheClientsPath()
	{
		var beatmaps = await fixture.Host.ImportSampleAsync("Sets/padoru-standard.osz");
		var set = beatmaps.First().Beatmapset;
		var (manager, _) = await fixture.SignInAsync("ImgManager", UserPermissionsGroups.Default | UserPermissions.BeatmapsetManage);
		using (manager)
		using (var anon = Anonymous())
		{
			using var cover = await anon.GetFromAsync("assets", $"/beatmapsets/{set.Id}/covers/card.jpg");
			using var thumb = await anon.GetFromAsync("b", $"/thumb/{set.Id}.jpg");
			await manager.CallAsync(HttpMethod.Patch, $"/v1/beatmapsets/{set.Id}", new { visible = false });
			using var hidden = await anon.GetFromAsync("assets", $"/beatmapsets/{set.Id}/covers/list.jpg");
			await manager.CallAsync(HttpMethod.Patch, $"/v1/beatmapsets/{set.Id}", new { visible = true });

			Assert.Equal(200, (int)cover.StatusCode);
			Assert.Equal(200, (int)thumb.StatusCode);
			Assert.Equal(404, (int)hidden.StatusCode);
		}
	}

	private async Task<int> PutFileAsync(RestClient client, string path, byte[] content)
	{
		using var form = new MultipartFormDataContent();
		var file = new ByteArrayContent(content);
		file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
		form.Add(file, "file", "avatar.png");
		using var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = form };
		request.Headers.Host = $"api.{fixture.Host.Domain}";
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", client.Bearer);
		using var http = new HttpClient { BaseAddress = fixture.Host.Url };
		using var response = await http.SendAsync(request);
		return (int)response.StatusCode;
	}
}
