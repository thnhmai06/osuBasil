using Basil.Application.Content.Persistence;
using Basil.Domain.Users;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Rest.Tests.Users;

/// <summary>Closing sessions, changing passwords and the profiles lock (plan 5.5).</summary>
[Collection("REST host")]
public sealed class SessionTests(RestHostFixture fixture)
{
	private async Task<RestClient> SecondClientAsync(string name)
	{
		var client = new RestClient(fixture.Host.Url, fixture.Host.Domain);
		await client.LoginAsync(name);
		return client;
	}

	[Fact]
	public async Task GetSession_OfOwn_Works_OfAnotherUserIs403_AndAnUnknownIdIs404()
	{
		var (client, id) = await fixture.SignInAsync("SesGet");
		var other = await fixture.Host.CreateUserAsync("SesGetOther");
		using (client)
		{
			var (_, session) = await client.CallAsync(HttpMethod.Get, "/v1/auth/session");
			var sid = (string)session!["data"]!["sessionId"]!;

			var (own, view) = await client.CallAsync(HttpMethod.Get, $"/v1/users/{id}/sessions/{sid}");
			var (foreign, _) = await client.CallAsync(HttpMethod.Get, $"/v1/users/{other.Id}/sessions/{sid}");
			var (unknown, _) = await client.CallAsync(HttpMethod.Get, $"/v1/users/{id}/sessions/{Guid.NewGuid()}");

			Assert.Equal(200, own);
			Assert.Equal("api", (string)view!["data"]!["type"]!);
			Assert.Equal(403, foreign);
			Assert.Equal(404, unknown);
		}
	}

	[Fact]
	public async Task CloseAll_KeepsTheCurrentSessionByDefault_AndClosesItWhenAsked()
	{
		var (first, id) = await fixture.SignInAsync("SesAll");
		using var second = await SecondClientAsync("SesAll");
		using (first)
		{
			var (closed, _) = await first.CallAsync(HttpMethod.Delete, $"/v1/users/{id}/sessions");
			var (firstStill, _) = await first.CallAsync(HttpMethod.Get, "/v1/auth/session");
			var (secondGone, _) = await second.CallAsync(HttpMethod.Get, "/v1/auth/session");
			var (everything, _) = await first.CallAsync(HttpMethod.Delete, $"/v1/users/{id}/sessions", new { keepCurrent = false });
			var (firstGone, _) = await first.CallAsync(HttpMethod.Get, "/v1/auth/session");

			Assert.Equal(200, closed);
			Assert.Equal(200, firstStill);
			Assert.Equal(401, secondGone);
			Assert.Equal(200, everything);
			Assert.Equal(401, firstGone);
		}
	}

	[Fact]
	public async Task CloseOne_RevokesThatToken_AndStaffCloseEverySessionOfAnotherUser()
	{
		var (first, id) = await fixture.SignInAsync("SesOne");
		using var second = await SecondClientAsync("SesOne");
		var (staff, _) = await fixture.SignInAsync("SesStaff", RestHostFixture.StaffPermissions);
		using (first)
		using (staff)
		{
			var (_, session) = await second.CallAsync(HttpMethod.Get, "/v1/auth/session");
			var (one, _) = await first.CallAsync(HttpMethod.Delete, $"/v1/users/{id}/sessions/{(string)session!["data"]!["sessionId"]!}");
			var (secondGone, _) = await second.CallAsync(HttpMethod.Get, "/v1/auth/session");
			var (byStaff, _) = await staff.CallAsync(HttpMethod.Delete, $"/v1/users/{id}/sessions");
			var (firstGone, _) = await first.CallAsync(HttpMethod.Get, "/v1/auth/session");

			Assert.Equal(200, one);
			Assert.Equal(401, secondGone);
			Assert.Equal(200, byStaff);
			Assert.Equal(401, firstGone);
		}
	}

	[Fact]
	public async Task BasilBot_CannotHaveItsSessionsClosedOrAnIrcKeyOpened()
	{
		var (staff, _) = await fixture.SignInAsync("SesBotStaff", RestHostFixture.StaffPermissions);
		using (staff)
		{
			var (close, envelope) = await staff.CallAsync(HttpMethod.Delete, "/v1/users/1/sessions");
			var (password, _) = await staff.CallAsync(HttpMethod.Put, "/v1/users/1/password", new { @new = "x" });

			Assert.Equal(409, close);
			Assert.Equal("systemAccount", (string)envelope!["errors"]!["code"]!);
			Assert.Equal(409, password);
		}
	}

	[Fact]
	public async Task Password_WithIncludeCurrent_ClosesTheCallingSessionToo_AndStaffChangeClosesEverySession()
	{
		var (first, id) = await fixture.SignInAsync("SesPwInc");
		var (staff, _) = await fixture.SignInAsync("SesPwStaff", RestHostFixture.StaffPermissions);
		using (first)
		using (staff)
		{
			var (own, _) = await first.CallAsync(HttpMethod.Put, $"/v1/users/{id}/password",
				new { current = "password", @new = "second", includeCurrent = true });
			var (firstGone, _) = await first.CallAsync(HttpMethod.Get, "/v1/auth/session");

			using var again = new RestClient(fixture.Host.Url, fixture.Host.Domain);
			await again.LoginAsync("SesPwInc", "second");
			var (byStaff, _) = await staff.CallAsync(HttpMethod.Put, $"/v1/users/{id}/password", new { @new = "third" });
			var (againGone, _) = await again.CallAsync(HttpMethod.Get, "/v1/auth/session");

			Assert.Equal(200, own);
			Assert.Equal(401, firstGone);
			Assert.Equal(200, byStaff);
			Assert.Equal(401, againGone);
		}
	}

	[Fact]
	public async Task ProfilesLock_BlocksTheOwnerFromChangingTheNameAndPassword_ButNotStaff()
	{
		await using var host = await BasilHost.StartAsync(fixture.Database);
		await RestHostFixture.RaiseLimitsAsync(host);
		var owner = await host.CreateUserAsync("SesLocked");
		await host.CreateUserAsync("SesLockStaff", "password", RestHostFixture.StaffPermissions);
		using var ownerClient = new RestClient(host.Url, host.Domain);
		using var staffClient = new RestClient(host.Url, host.Domain);
		await ownerClient.LoginAsync("SesLocked");
		await staffClient.LoginAsync("SesLockStaff");
		var settings = host.Services.GetRequiredService<ISettingsRepository>();
		await settings.CreateOrUpdateAsync((await settings.GetAsync()) with { Locks = Locks.Profiles });

		var (rename, renameBody) = await ownerClient.CallAsync(HttpMethod.Patch, $"/v1/users/{owner.Id}", new { name = "NewName1" });
		var (password, _) = await ownerClient.CallAsync(HttpMethod.Put, $"/v1/users/{owner.Id}/password", new { current = "password", @new = "x" });
		var (byStaff, _) = await staffClient.CallAsync(HttpMethod.Patch, $"/v1/users/{owner.Id}", new { name = "NewName2" });

		Assert.Equal(409, rename);
		Assert.Equal("locked", (string)renameBody!["errors"]!["code"]!);
		Assert.Equal(409, password);
		Assert.Equal(200, byStaff);
	}

	[Fact]
	public async Task RoomsLock_BlocksCreatingARoom_ForNonManagers()
	{
		await using var host = await BasilHost.StartAsync(fixture.Database);
		await RestHostFixture.RaiseLimitsAsync(host);
		await host.CreateUserAsync("SesRoomLock");
		using var client = new RestClient(host.Url, host.Domain);
		await client.LoginAsync("SesRoomLock");
		var settings = host.Services.GetRequiredService<ISettingsRepository>();
		await settings.CreateOrUpdateAsync((await settings.GetAsync()) with { Locks = Locks.Rooms });

		var (status, envelope) = await client.CallAsync(HttpMethod.Post, "/v1/rooms", new { name = "Locked out" });

		Assert.Equal(409, status);
		Assert.Equal("locked", (string)envelope!["errors"]!["code"]!);
	}
}
