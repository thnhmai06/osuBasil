using Basil.Application.Content.Persistence;
using Basil.Domain.Users;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Rest.Tests.Content;

/// <summary>Settings by category, announcements and screenshots.</summary>
[Collection("REST host")]
public sealed class ContentTests(RestHostFixture fixture)
{
	private static readonly UserPermissions Settings = UserPermissionsGroups.Default | UserPermissions.ServerManageSettings;

	[Fact]
	public async Task Settings_WithoutPermission_Answer403WithTheMissingMask_AndWithoutAToken401()
	{
		var (client, _) = await fixture.SignInAsync("SetPlain");
		using (client)
		using (var anon = new RestClient(fixture.Host.Url, fixture.Host.Domain))
		{
			var (denied, envelope) = await client.CallAsync(HttpMethod.Get, "/v1/settings");
			var (deniedPatch, _) = await client.CallAsync(HttpMethod.Patch, "/v1/settings/general", new { motd = "x" });
			var (noToken, _) = await anon.CallAsync(HttpMethod.Get, "/v1/settings");

			Assert.Equal(403, denied);
			Assert.Equal((ulong)UserPermissions.ServerManageSettings, (ulong)envelope!["errors"]!["missing"]!);
			Assert.Equal(403, deniedPatch);
			Assert.Equal(401, noToken);
		}
	}

	[Fact]
	public async Task Settings_Get_ListsEveryCategory_AndAnUnknownCategoryAnswers404()
	{
		var (client, _) = await fixture.SignInAsync("SetReader", Settings);
		using (client)
		{
			var (all, envelope) = await client.CallAsync(HttpMethod.Get, "/v1/settings");
			var (one, rate) = await client.CallAsync(HttpMethod.Get, "/v1/settings/rateLimits");
			var (unknown, _) = await client.CallAsync(HttpMethod.Get, "/v1/settings/nothing");

			Assert.Equal(200, all);
			foreach (var name in new[] { "general", "locks", "sessions", "rateLimits", "uploads", "beatmaps", "multiplayer", "bot", "cors" })
				Assert.NotNull(envelope!["data"]![name]);
			Assert.Equal(200, one);
			Assert.NotNull(rate!["data"]!["login"]);
			Assert.Equal(404, unknown);
		}
	}

	[Fact]
	public async Task Settings_Patch_MergesTheGivenFields_AppliesAtOnce_AndRefusesInvalidValues()
	{
		var (client, _) = await fixture.SignInAsync("SetWriter", Settings);
		using (client)
		{
			var (patched, envelope) = await client.CallAsync(HttpMethod.Patch, "/v1/settings/bot", new { commandPrefix = "?" });
			var stored = await fixture.Host.Services.GetRequiredService<ISettingsRepository>().GetAsync();
			var (invalid, _) = await client.CallAsync(HttpMethod.Patch, "/v1/settings/sessions", new { idleWindow = "00:00:00" });
			var (restored, _) = await client.CallAsync(HttpMethod.Patch, "/v1/settings/bot", new { commandPrefix = "!" });

			Assert.Equal(200, patched);
			Assert.Equal("?", (string)envelope!["data"]!["commandPrefix"]!);
			Assert.Equal("?", stored.Bot.CommandPrefix);
			Assert.Equal(422, invalid);
			Assert.Equal(200, restored);
		}
	}

	[Fact]
	public async Task Settings_Locks_AreSwitchedAsFlags()
	{
		var (client, _) = await fixture.SignInAsync("SetLocks", Settings);
		using (client)
		{
			var (patched, envelope) = await client.CallAsync(HttpMethod.Patch, "/v1/settings/locks", new { profiles = true });
			var stored = await fixture.Host.Services.GetRequiredService<ISettingsRepository>().GetAsync();
			await client.CallAsync(HttpMethod.Patch, "/v1/settings/locks", new { profiles = false });

			Assert.Equal(200, patched);
			Assert.True((bool)envelope!["data"]!["profiles"]!);
			Assert.True((bool)envelope["data"]!["beatmapsets"]!);
			Assert.True(stored.Locks.HasFlag(Locks.Profiles));
		}
	}

	[Fact]
	public async Task Announce_NeedsThePermission_AValidBody_AndKnownRecipients()
	{
		var (plain, _) = await fixture.SignInAsync("AnnPlain");
		var (staff, _) = await fixture.SignInAsync("AnnStaff", UserPermissionsGroups.Default | UserPermissions.ChatManageAnnounce);
		using (plain)
		using (staff)
		{
			var (denied, _) = await plain.CallAsync(HttpMethod.Post, "/v1/announce", new { text = "hi" });
			var (all, envelope) = await staff.CallAsync(HttpMethod.Post, "/v1/announce", new { text = "hi" });
			var (empty, _) = await staff.CallAsync(HttpMethod.Post, "/v1/announce", new { text = " " });
			var (unknown, _) = await staff.CallAsync(HttpMethod.Post, "/v1/announce", new { text = "hi", to = new[] { 999999 } });
			var (some, _) = await staff.CallAsync(HttpMethod.Post, "/v1/announce", new { text = "hi", to = new[] { 1 } });

			Assert.Equal(403, denied);
			Assert.Equal(200, all);
			Assert.True((int)envelope!["data"]!["sent"]! >= 0);
			Assert.Equal(422, empty);
			Assert.Equal(404, unknown);
			Assert.Equal(200, some);
		}
	}

	[Fact]
	public async Task Screenshots_ListAndDelete_NeedTheManagePermission()
	{
		var reference = await fixture.Host.Services.GetRequiredService<IScreenshotStorage>()
			.SaveAsync(new MemoryStream([1, 2, 3]), "png");
		var id = Path.GetFileName(reference.Value);
		var (plain, _) = await fixture.SignInAsync("ShotPlain");
		var (moderator, _) = await fixture.SignInAsync("ShotMod", UserPermissionsGroups.Default | UserPermissions.ScreenshotManage);
		using (plain)
		using (moderator)
		{
			var (denied, _) = await plain.CallAsync(HttpMethod.Get, "/v1/screenshots");
			var (deniedDelete, _) = await plain.CallAsync(HttpMethod.Delete, $"/v1/screenshots/{id}");
			var (listed, envelope) = await moderator.CallAsync(HttpMethod.Get, "/v1/screenshots");
			var (deleted, _) = await moderator.CallAsync(HttpMethod.Delete, $"/v1/screenshots/{id}");
			var (again, _) = await moderator.CallAsync(HttpMethod.Delete, $"/v1/screenshots/{id}");

			Assert.Equal(403, denied);
			Assert.Equal(403, deniedDelete);
			Assert.Equal(200, listed);
			Assert.Contains(envelope!["data"]!.AsArray(), item => (string)item!["id"]! == id);
			Assert.Equal(200, deleted);
			Assert.Equal(404, again);
		}
	}
}
