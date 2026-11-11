using Basil.Application.Common.Settings;
using Basil.Application.Content.Storage;
using Basil.Domain.Users;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Rest.Tests.Content;

/// <summary>Settings by category, announcements and screenshots.</summary>
[Collection("REST host")]
public sealed class ContentTests(RestHostFixture fixture)
{
	private static readonly UserPrivileges Settings =
		UserPrivilegesExtensions.Default | UserPrivileges.Developer;

	[Fact]
	public async Task SettingsRefusals_Answer403401()
	{
		var (client, _) = await fixture.SignInAsync("SetPlain");
		using (client)
		using (var anon = new RestClient(fixture.Host.Url, fixture.Host.Domain))
		{
			var (denied, envelope) = await client.CallAsync(HttpMethod.Get, "/v1/settings");
			var (deniedPatch, _) = await client.CallAsync(HttpMethod.Patch, "/v1/settings/general", new { motd = "x" });
			var (noToken, _) = await anon.CallAsync(HttpMethod.Get, "/v1/settings");

			Assert.Equal(403, denied);
			Assert.Equal((ulong)UserPrivileges.Developer, (ulong)envelope!["errors"]!["missing"]!);
			Assert.Equal(403, deniedPatch);
			Assert.Equal(401, noToken);
		}
	}

	[Fact]
	public async Task SettingsCategories_ListedAnd404()
	{
		var (client, _) = await fixture.SignInAsync("SetReader", Settings);
		using (client)
		{
			var (all, envelope) = await client.CallAsync(HttpMethod.Get, "/v1/settings");
			var (one, rate) = await client.CallAsync(HttpMethod.Get, "/v1/settings/rateLimits");
			var (unknown, _) = await client.CallAsync(HttpMethod.Get, "/v1/settings/nothing");

			Assert.Equal(200, all);
			foreach (var name in new[]
			         {
				         "general", "locks", "sessions", "rateLimits", "uploads", "beatmaps", "multiplayer", "bot",
				         "cors"
			         })
				Assert.NotNull(envelope!["data"]![name]);
			Assert.Equal(200, one);
			Assert.NotNull(rate!["data"]!["login"]);
			Assert.Equal(404, unknown);
		}
	}

	[Fact]
	public async Task SettingsPatch_MergesAppliesRefuses()
	{
		var (client, _) = await fixture.SignInAsync("SetWriter", Settings);
		using (client)
		{
			var (patched, envelope) =
				await client.CallAsync(HttpMethod.Patch, "/v1/settings/bot", new { commandPrefix = "?" });
			var stored = await fixture.Host.Services.GetRequiredService<ISettingsRepository>().GetAsync(TestContext.Current.CancellationToken);
			var (invalid, _) = await client.CallAsync(HttpMethod.Patch, "/v1/settings/sessions",
				new { retentionPeriod = "00:00:00" });
			var (restored, _) =
				await client.CallAsync(HttpMethod.Patch, "/v1/settings/bot", new { commandPrefix = "!" });

			Assert.Equal(200, patched);
			Assert.Equal("?", (string)envelope!["data"]!["commandPrefix"]!);
			Assert.Equal("?", stored.Bot.CommandPrefix);
			Assert.Equal(422, invalid);
			Assert.Equal(200, restored);
		}
	}

	[Fact]
	public async Task SessionLimitsPatch_RoundTripsRefuses()
	{
		var (client, _) = await fixture.SignInAsync("SetSessions", Settings);
		using (client)
		{
			var (patched, envelope) = await client.CallAsync(HttpMethod.Patch, "/v1/settings/sessions",
				new { maxPerUser = new { irc = 7 } });
			var stored = await fixture.Host.Services.GetRequiredService<ISettingsRepository>().GetAsync(TestContext.Current.CancellationToken);
			var (invalid, _) = await client.CallAsync(HttpMethod.Patch, "/v1/settings/sessions",
				new { maxPerUser = new { api = 0 } });
			var (restored, _) = await client.CallAsync(HttpMethod.Patch, "/v1/settings/sessions",
				new { maxPerUser = new { irc = 5 } });

			Assert.Equal(200, patched);
			Assert.Equal(7, (int)envelope!["data"]!["maxPerUser"]!["irc"]!);
			Assert.Equal(1, (int)envelope["data"]!["maxPerUser"]!["stable"]!);
			Assert.Equal(7, stored.Sessions.MaxPerUser.Irc);
			Assert.Equal(422, invalid);
			Assert.Equal(200, restored);
		}
	}

	[Fact]
	public async Task SettingsLocks_SwitchAsFlags()
	{
		var (client, _) = await fixture.SignInAsync("SetLocks", Settings);
		using (client)
		{
			var (patched, envelope) =
				await client.CallAsync(HttpMethod.Patch, "/v1/settings/locks", new { profiles = true });
			var stored = await fixture.Host.Services.GetRequiredService<ISettingsRepository>().GetAsync(TestContext.Current.CancellationToken);
			await client.CallAsync(HttpMethod.Patch, "/v1/settings/locks", new { profiles = false });

			Assert.Equal(200, patched);
			Assert.True((bool)envelope!["data"]!["profiles"]!);
			Assert.True((bool)envelope["data"]!["beatmapsets"]!);
			Assert.True(stored.Locks.HasFlag(Locks.Profiles));
		}
	}

	[Fact]
	public async Task AnnounceNeeds_PrivilegeAndBody()
	{
		var (plain, _) = await fixture.SignInAsync("AnnPlain");
		var (staff, _) = await fixture.SignInAsync("AnnStaff",
			UserPrivilegesExtensions.Default | UserPrivileges.Moderator);
		using (plain)
		using (staff)
		{
			var (denied, _) = await plain.CallAsync(HttpMethod.Post, "/v1/announce", new { text = "hi" });
			var (all, envelope) = await staff.CallAsync(HttpMethod.Post, "/v1/announce", new { text = "hi" });
			var (empty, _) = await staff.CallAsync(HttpMethod.Post, "/v1/announce", new { text = " " });
			var (unknown, _) = await staff.CallAsync(HttpMethod.Post, "/v1/announce",
				new { text = "hi", to = new[] { 999999 } });
			var (some, _) =
				await staff.CallAsync(HttpMethod.Post, "/v1/announce", new { text = "hi", to = new[] { 1 } });

			Assert.Equal(403, denied);
			Assert.Equal(200, all);
			Assert.True((int)envelope!["data"]!["sent"]! >= 0);
			Assert.Equal(422, empty);
			Assert.Equal(404, unknown);
			Assert.Equal(200, some);
		}
	}

	[Fact]
	public async Task Screenshots_NeedManagePrivilege()
	{
		var reference = await fixture.Host.Services.GetRequiredService<IScreenshotStorage>()
			.SaveAsync(new MemoryStream([1, 2, 3]), "png");
		var id = Path.GetFileName(reference.Value);
		var (plain, _) = await fixture.SignInAsync("ShotPlain");
		var (moderator, _) =
			await fixture.SignInAsync("ShotMod", UserPrivilegesExtensions.Default | UserPrivileges.Moderator);
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