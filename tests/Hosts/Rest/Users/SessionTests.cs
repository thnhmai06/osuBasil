using Basil.Application.Shared.Settings;
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
	public async Task GetOwn_Works()
	{
		var (client, id) = await fixture.SignInAsync("SesGet");
		var other = await fixture.Host.CreateUserAsync("SesGetOther");
		using (client)
		{
			var (_, session) = await client.CallAsync(HttpMethod.Get, "/v1/auth/session");
			var sid = (long)session!["data"]!["sessionId"]!;

			var (own, view) = await client.CallAsync(HttpMethod.Get, $"/v1/users/{id}/sessions/{sid}");
			var (foreign, _) = await client.CallAsync(HttpMethod.Get, $"/v1/users/{other.Id}/sessions/{sid}");
			var (unknown, _) = await client.CallAsync(HttpMethod.Get, $"/v1/users/{id}/sessions/{long.MaxValue}");

			Assert.Equal(200, own);
			Assert.Equal("api", (string)view!["data"]!["type"]!);
			Assert.Equal(403, foreign);
			Assert.Equal(404, unknown);
		}
	}

	[Fact]
	public async Task ConnectionsOwn_Listed()
	{
		var (client, id) = await fixture.SignInAsync("SesTrail");
		var other = await fixture.Host.CreateUserAsync("SesTrailOther");
		using (client)
		{
			var (_, session) = await client.CallAsync(HttpMethod.Get, "/v1/auth/session");
			var sid = (long)session!["data"]!["sessionId"]!;

			var (own, trail) = await client.CallAsync(HttpMethod.Get, $"/v1/users/{id}/sessions/{sid}/connections");
			var (foreign, _) = await client.CallAsync(HttpMethod.Get,
				$"/v1/users/{other.Id}/sessions/{sid}/connections");
			var (unknown, _) = await client.CallAsync(HttpMethod.Get,
				$"/v1/users/{id}/sessions/{long.MaxValue}/connections");

			Assert.Equal(200, own);
			var connection = Assert.Single(trail!["data"]!.AsArray());
			Assert.NotNull(connection!["ip"]);
			Assert.NotNull(connection["openedAt"]);
			Assert.Null(connection["closedAt"]);
			Assert.Null(connection["closeReason"]);
			Assert.False((bool)trail["meta"]!["hasNext"]!);
			Assert.Equal(403, foreign);
			Assert.Equal(404, unknown);
		}
	}

	[Fact]
	public async Task ConnectionsClosed_ShowReason()
	{
		var (first, id) = await fixture.SignInAsync("SesTrailEnd");
		using var second = await SecondClientAsync("SesTrailEnd");
		using (first)
		{
			var (_, session) = await second.CallAsync(HttpMethod.Get, "/v1/auth/session");
			var sid = (long)session!["data"]!["sessionId"]!;
			await first.CallAsync(HttpMethod.Delete, $"/v1/users/{id}/sessions/{sid}");

			var (_, trail) = await first.CallAsync(HttpMethod.Get, $"/v1/users/{id}/sessions/{sid}/connections");

			var connection = Assert.Single(trail!["data"]!.AsArray());
			Assert.NotNull(connection!["closedAt"]);
			Assert.NotNull(connection["closeReason"]);
		}
	}

	[Fact]
	public async Task CloseAllDefault_KeepsCurrent()
	{
		var (first, id) = await fixture.SignInAsync("SesAll");
		using var second = await SecondClientAsync("SesAll");
		using (first)
		{
			var (closed, _) = await first.CallAsync(HttpMethod.Delete, $"/v1/users/{id}/sessions");
			var (firstStill, _) = await first.CallAsync(HttpMethod.Get, "/v1/auth/session");
			var (secondGone, _) = await second.CallAsync(HttpMethod.Get, "/v1/auth/session");
			var (everything, _) = await first.CallAsync(HttpMethod.Delete, $"/v1/users/{id}/sessions",
				new { keepCurrent = false });
			var (firstGone, _) = await first.CallAsync(HttpMethod.Get, "/v1/auth/session");

			Assert.Equal(200, closed);
			Assert.Equal(200, firstStill);
			Assert.Equal(401, secondGone);
			Assert.Equal(200, everything);
			Assert.Equal(401, firstGone);
		}
	}

	[Fact]
	public async Task InvalidBody_Refused()
	{
		var (first, id) = await fixture.SignInAsync("SesBadBody");
		using var second = await SecondClientAsync("SesBadBody");
		using (first)
		{
			var (refused, _) = await first.CallAsync(HttpMethod.Delete, $"/v1/users/{id}/sessions", "not an object");
			var (secondStill, _) = await second.CallAsync(HttpMethod.Get, "/v1/auth/session");

			Assert.Equal(422, refused);
			Assert.Equal(200, secondStill);
		}
	}

	[Fact]
	public async Task CloseOne_RevokesStaffCloses()
	{
		var (first, id) = await fixture.SignInAsync("SesOne");
		using var second = await SecondClientAsync("SesOne");
		var (staff, _) = await fixture.SignInAsync("SesStaff", RestHostFixture.StaffPrivileges);
		using (first)
		using (staff)
		{
			var (_, session) = await second.CallAsync(HttpMethod.Get, "/v1/auth/session");
			var (one, _) = await first.CallAsync(HttpMethod.Delete,
				$"/v1/users/{id}/sessions/{(long)session!["data"]!["sessionId"]!}");
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
	public async Task BasilBot_NoCloseOpen()
	{
		var (staff, _) = await fixture.SignInAsync("SesBotStaff", RestHostFixture.StaffPrivileges);
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
	public async Task PasswordIncludeCurrent_ClosesAll()
	{
		var (first, id) = await fixture.SignInAsync("SesPwInc");
		var (staff, _) = await fixture.SignInAsync("SesPwStaff", RestHostFixture.StaffPrivileges);
		using (first)
		using (staff)
		{
			var (own, _) = await first.CallAsync(HttpMethod.Put, $"/v1/users/{id}/password",
				new { current = "password", @new = "second", includeCurrent = true });
			var (firstGone, _) = await first.CallAsync(HttpMethod.Get, "/v1/auth/session");

			using var again = new RestClient(fixture.Host.Url, fixture.Host.Domain);
			await again.LoginAsync("SesPwInc", "second");
			var (byStaff, _) =
				await staff.CallAsync(HttpMethod.Put, $"/v1/users/{id}/password", new { @new = "third" });
			var (againGone, _) = await again.CallAsync(HttpMethod.Get, "/v1/auth/session");

			Assert.Equal(200, own);
			Assert.Equal(401, firstGone);
			Assert.Equal(200, byStaff);
			Assert.Equal(401, againGone);
		}
	}

	[Fact]
	public async Task ProfilesLock_BlocksOwner()
	{
		await using var host = await BasilHost.StartAsync(fixture.Database);
		await RestHostFixture.RaiseLimitsAsync(host);
		var owner = await host.CreateUserAsync("SesLocked");
		await host.CreateUserAsync("SesLockStaff", "password", RestHostFixture.StaffPrivileges);
		using var ownerClient = new RestClient(host.Url, host.Domain);
		using var staffClient = new RestClient(host.Url, host.Domain);
		await ownerClient.LoginAsync("SesLocked");
		await staffClient.LoginAsync("SesLockStaff");
		var settings = host.Services.GetRequiredService<ISettingsRepository>();
		await settings.CreateOrUpdateAsync(await settings.GetAsync(TestContext.Current.CancellationToken) with
		{
			Locks = Locks.Profiles
		});

		var (rename, renameBody) =
			await ownerClient.CallAsync(HttpMethod.Patch, $"/v1/users/{owner.Id}", new { name = "NewName1" });
		var (password, _) = await ownerClient.CallAsync(HttpMethod.Put, $"/v1/users/{owner.Id}/password",
			new { current = "password", @new = "x" });
		var (byStaff, _) =
			await staffClient.CallAsync(HttpMethod.Patch, $"/v1/users/{owner.Id}", new { name = "NewName2" });

		Assert.Equal(409, rename);
		Assert.Equal("locked", (string)renameBody!["errors"]!["code"]!);
		Assert.Equal(409, password);
		Assert.Equal(200, byStaff);
	}

	[Fact]
	public async Task RoomsLock_BlocksNonManagers()
	{
		await using var host = await BasilHost.StartAsync(fixture.Database);
		await RestHostFixture.RaiseLimitsAsync(host);
		await host.CreateUserAsync("SesRoomLock");
		using var client = new RestClient(host.Url, host.Domain);
		await client.LoginAsync("SesRoomLock");
		var settings = host.Services.GetRequiredService<ISettingsRepository>();
		await settings.CreateOrUpdateAsync(await settings.GetAsync(TestContext.Current.CancellationToken) with
		{
			Locks = Locks.Rooms
		});

		var (status, envelope) = await client.CallAsync(HttpMethod.Post, "/v1/rooms", new { name = "Locked out" });

		Assert.Equal(409, status);
		Assert.Equal("locked", (string)envelope!["errors"]!["code"]!);
	}

	[Fact]
	public async Task UnknownNoPrivilege_403()
	{
		var (client, _) = await fixture.SignInAsync("SesUnknownUser");
		var other = await fixture.Host.CreateUserAsync("SesUnknownOther");
		using (client)
		{
			var (existing, _) = await client.CallAsync(HttpMethod.Get, $"/v1/users/{other.Id}/sessions");
			var (unknown, _) = await client.CallAsync(HttpMethod.Get, "/v1/users/9999999/sessions");

			Assert.Equal(403, existing);
			Assert.Equal(403, unknown);
		}
	}
}