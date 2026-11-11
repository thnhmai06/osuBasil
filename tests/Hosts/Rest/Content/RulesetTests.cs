using Basil.Domain.Users;

namespace Basil.Host.Rest.Tests.Content;

/// <summary>The version routes, the ruleset routes and the beatmap reload.</summary>
[Collection("REST host")]
public sealed class RulesetTests(RestHostFixture fixture)
{
	private static readonly UserPrivileges Manager =
		UserPrivilegesExtensions.Default | UserPrivileges.Developer;

	[Fact]
	public async Task RulesetVersion_PublicAndNamed()
	{
		using var anon = new RestClient(fixture.Host.Url, fixture.Host.Domain);

		var (status, envelope) = await anon.CallAsync(HttpMethod.Get, "/v1/version");

		Assert.Equal(200, status);
		Assert.False(string.IsNullOrEmpty((string?)envelope!["data"]!["app"]));
		Assert.False(string.IsNullOrEmpty((string?)envelope["data"]!["ruleset"]));
	}

	[Fact]
	public async Task StagedVersion_NeedsTokenPrivilege()
	{
		var (manager, _) = await fixture.SignInAsync("RulesetManager", Manager);
		var (plain, _) = await fixture.SignInAsync("RulesetPlain");
		using (manager)
		using (plain)
		using (var anon = new RestClient(fixture.Host.Url, fixture.Host.Domain))
		{
			var (unauthorized, _) = await anon.CallAsync(HttpMethod.Get, "/v1/settings/ruleset/version");
			var (denied, refusal) = await plain.CallAsync(HttpMethod.Get, "/v1/settings/ruleset/version");
			var (ok, envelope) = await manager.CallAsync(HttpMethod.Get, "/v1/settings/ruleset/version");

			Assert.Equal(401, unauthorized);
			Assert.Equal(403, denied);
			Assert.Equal((ulong)UserPrivileges.Developer, (ulong)refusal!["errors"]!["missing"]!);
			Assert.Equal(200, ok);
			Assert.False(string.IsNullOrEmpty((string?)envelope!["data"]!["active"]));
			Assert.Null(envelope["data"]!["staged"]);
		}
	}

	[Fact]
	public async Task PlainUserUpdate_Answers403Mask()
	{
		var (plain, _) = await fixture.SignInAsync("RulesetUpdPlain");
		using (plain)
		{
			var (denied, refusal) = await plain.CallAsync(HttpMethod.Post, "/v1/settings/ruleset/update");

			Assert.Equal(403, denied);
			Assert.Equal((ulong)UserPrivileges.Developer, (ulong)refusal!["errors"]!["missing"]!);
		}
	}

	[Fact]
	public async Task Reload_ManagerOnly()
	{
		var (manager, _) = await fixture.SignInAsync("ReloadManager", Manager);
		var (plain, _) = await fixture.SignInAsync("ReloadPlain");
		using (manager)
		using (plain)
		using (var anon = new RestClient(fixture.Host.Url, fixture.Host.Domain))
		{
			var (unauthorized, _) = await anon.CallAsync(HttpMethod.Post, "/v1/beatmapsets/reload");
			var (denied, _) = await plain.CallAsync(HttpMethod.Post, "/v1/beatmapsets/reload");
			var (accepted, _) = await manager.CallAsync(HttpMethod.Post, "/v1/beatmapsets/reload");

			Assert.Equal(401, unauthorized);
			Assert.Equal(403, denied);
			Assert.Equal(202, accepted);
		}
	}
}