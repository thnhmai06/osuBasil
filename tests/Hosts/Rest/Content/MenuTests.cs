using Basil.Domain.Users;

namespace Basil.Host.Rest.Tests.Content;

/// <summary>FAQ entries and the main menu: public to read, content managers to change.</summary>
[Collection("REST host")]
public sealed class MenuTests(RestHostFixture fixture)
{
	private static readonly UserPermissions Manager =
		UserPermissionsGroups.Default | UserPermissions.ServerManageContent;

	private static readonly byte[] Png =
		File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "Images", "avatar-default.png"));

	[Fact]
	public async Task Faq_IsPublicToRead_AndManagersWriteIt()
	{
		var (manager, _) = await fixture.SignInAsync("MenuFaqMgr", Manager);
		var (plain, _) = await fixture.SignInAsync("MenuFaqPlain");
		using (manager)
		using (plain)
		using (var anon = new RestClient(fixture.Host.Url, fixture.Host.Domain))
		{
			var (denied, _) = await plain.CallAsync(HttpMethod.Put, "/v1/faqs/rules", new { text = "x" });
			var (saved, _) = await manager.CallAsync(HttpMethod.Put, "/v1/faqs/rules", new { text = "Be kind." });
			var (read, entry) = await anon.CallAsync(HttpMethod.Get, "/v1/faqs/rules");
			var (list, names) = await anon.CallAsync(HttpMethod.Get, "/v1/faqs");
			var (missing, _) = await anon.CallAsync(HttpMethod.Get, "/v1/faqs/nothing");
			var (invalid, _) = await manager.CallAsync(HttpMethod.Put, "/v1/faqs/a::b", new { text = "x" });
			var (deleted, _) = await manager.CallAsync(HttpMethod.Delete, "/v1/faqs/rules");
			var (gone, _) = await anon.CallAsync(HttpMethod.Get, "/v1/faqs/rules");

			Assert.Equal(403, denied);
			Assert.Equal(200, saved);
			Assert.Equal(200, read);
			Assert.Equal("Be kind.", (string)entry!["data"]!["text"]!);
			Assert.Equal(200, list);
			Assert.Contains(names!["data"]!.AsArray(), n => (string)n! == "rules");
			Assert.Equal(404, missing);
			Assert.Equal(422, invalid);
			Assert.Equal(200, deleted);
			Assert.Equal(404, gone);
		}
	}

	[Fact]
	public async Task Banner_IsAddedByManagers_ListedPublicly_AndDeleted()
	{
		var (manager, _) = await fixture.SignInAsync("MenuBanMgr", Manager);
		var (plain, _) = await fixture.SignInAsync("MenuBanPlain");
		using (manager)
		using (plain)
		using (var anon = new RestClient(fixture.Host.Url, fixture.Host.Domain))
		{
			var (denied, _) = await plain.FormAsync(HttpMethod.Post, "/v1/menu/banners", ("image", "b.png", Png),
				("url", "https://example.org"));
			var (created, banner) = await manager.FormAsync(HttpMethod.Post, "/v1/menu/banners",
				("image", "menu-b.png", Png), ("url", "https://example.org"));
			var (noUrl, _) = await manager.FormAsync(HttpMethod.Post, "/v1/menu/banners", ("image", "menu-c.png", Png));
			var (listed, list) = await anon.CallAsync(HttpMethod.Get, "/v1/menu/banners");
			var image = (string)banner!["data"]!["image"]!;
			var (deleted, _) = await manager.CallAsync(HttpMethod.Delete, $"/v1/menu/banners/{image}");
			var (again, _) = await manager.CallAsync(HttpMethod.Delete, $"/v1/menu/banners/{image}");

			Assert.Equal(403, denied);
			Assert.Equal(201, created);
			Assert.Equal(422, noUrl);
			Assert.Equal(200, listed);
			Assert.Contains(list!["data"]!.AsArray(), b => (string)b!["image"]! == image && (bool)b["current"]!);
			Assert.Equal(200, deleted);
			Assert.Equal(404, again);
		}
	}

	[Fact]
	public async Task Seasonals_AndIcon_FollowTheSameRule()
	{
		var (manager, _) = await fixture.SignInAsync("MenuSeaMgr", Manager);
		var (plain, _) = await fixture.SignInAsync("MenuSeaPlain");
		using (manager)
		using (plain)
		using (var anon = new RestClient(fixture.Host.Url, fixture.Host.Domain))
		{
			var (denied, _) = await plain.FormAsync(HttpMethod.Post, "/v1/menu/seasonals", ("image", "s.png", Png));
			var (created, _) =
				await manager.FormAsync(HttpMethod.Post, "/v1/menu/seasonals", ("image", "winter.png", Png));
			var (listed, list) = await anon.CallAsync(HttpMethod.Get, "/v1/menu/seasonals");
			var (removed, _) = await manager.CallAsync(HttpMethod.Delete, "/v1/menu/seasonals/winter.png");
			var (iconDenied, _) = await plain.FormAsync(HttpMethod.Put, "/v1/menu/icon", ("image", "i.png", Png),
				("url", "https://example.org"));
			var (iconSet, _) = await manager.FormAsync(HttpMethod.Put, "/v1/menu/icon", ("image", "icon.png", Png),
				("url", "https://example.org"));
			var (icon, view) = await anon.CallAsync(HttpMethod.Get, "/v1/menu/icon");
			var (iconCleared, _) = await manager.CallAsync(HttpMethod.Delete, "/v1/menu/icon");

			Assert.Equal(403, denied);
			Assert.Equal(201, created);
			Assert.Contains(list!["data"]!.AsArray(), n => (string)n! == "winter.png");
			Assert.Equal(200, listed);
			Assert.Equal(200, removed);
			Assert.Equal(403, iconDenied);
			Assert.Equal(200, iconSet);
			Assert.Equal(200, icon);
			Assert.Equal("https://example.org/", (string)view!["data"]!["url"]!);
			Assert.Equal(200, iconCleared);
		}
	}
}