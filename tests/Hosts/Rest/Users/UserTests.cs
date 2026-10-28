using Basil.Domain.Users;

namespace Basil.Host.Rest.Tests.Users;

/// <summary>Accounts, sessions, restrictions and the redirects of the caller's own account.</summary>
[Collection("REST host")]
public sealed class UserTests(RestHostFixture fixture)
{
	private static int _pages;

	[Theory]
	[InlineData("/v1/users?limit=101")]
	[InlineData("/v1/users?limit=0")]
	[InlineData("/v1/users?offset=-1")]
	public async Task List_WithAPageOutOfBounds_Answers422(string path)
	{
		var (client, _) = await fixture.SignInAsync("Pg" + Interlocked.Increment(ref _pages));
		using (client)
		{
			var (status, _) = await client.CallAsync(HttpMethod.Get, path);

			Assert.Equal(422, status);
		}
	}

	[Fact]
	public async Task List_WithTheLargestPage_Answers200WithPagingMeta()
	{
		var (client, _) = await fixture.SignInAsync("PageMax");
		using (client)
		{
			var (status, envelope) = await client.CallAsync(HttpMethod.Get, "/v1/users?limit=100");

			Assert.Equal(200, status);
			Assert.Equal(0, (int)envelope!["meta"]!["offset"]!);
			Assert.Equal(100, (int)envelope["meta"]!["limit"]!);
		}
	}

	[Fact]
	public async Task Search_WithoutAToken_Answers401()
	{
		using var client = new RestClient(fixture.Host.Url, fixture.Host.Domain);

		var (status, _) = await client.CallAsync(HttpMethod.Get, "/v1/users");

		Assert.Equal(401, status);
	}

	[Fact]
	public async Task Get_UnknownUser_Answers404()
	{
		using var client = new RestClient(fixture.Host.Url, fixture.Host.Domain);

		var (status, _) = await client.CallAsync(HttpMethod.Get, "/v1/users/999999");

		Assert.Equal(404, status);
	}

	[Fact]
	public async Task Patch_OwnName_Changes()
	{
		var (client, id) = await fixture.SignInAsync("PatchSelf");
		using (client)
		{
			var (status, _) = await client.CallAsync(HttpMethod.Patch, $"/v1/users/{id}", new { name = "PatchedSelf" });
			var (_, user) = await client.CallAsync(HttpMethod.Get, $"/v1/users/{id}");

			Assert.Equal(200, status);
			Assert.Equal("PatchedSelf", (string)user!["data"]!["name"]!);
		}
	}

	[Fact]
	public async Task Patch_AnotherUserWithoutPermission_Answers403WithTheMissingMask()
	{
		var (client, _) = await fixture.SignInAsync("PatchOther");
		var victim = await fixture.Host.CreateUserAsync("PatchVictim");
		using (client)
		{
			var (status, envelope) = await client.CallAsync(HttpMethod.Patch, $"/v1/users/{victim.Id}",
				new { name = "Stolen" });

			Assert.Equal(403, status);
			Assert.Equal("permissionDenied", (string)envelope!["errors"]!["code"]!);
			Assert.NotEqual(0UL, (ulong)envelope["errors"]!["missing"]!);
		}
	}

	[Fact]
	public async Task Patch_BasilBotAsStaff_Answers409SystemAccount()
	{
		var (client, _) = await fixture.SignInAsync("PatchBotStaff", RestHostFixture.StaffPermissions);
		using (client)
		{
			var (status, envelope) = await client.CallAsync(HttpMethod.Delete, "/v1/users/1");

			Assert.Equal(409, status);
			Assert.Equal("systemAccount", (string)envelope!["errors"]!["code"]!);
		}
	}

	[Fact]
	public async Task Patch_StaffRenamesAnotherUser_Succeeds()
	{
		var (client, _) = await fixture.SignInAsync("PatchStaff", RestHostFixture.StaffPermissions);
		var target = await fixture.Host.CreateUserAsync("PatchTarget");
		using (client)
		{
			var (status, _) = await client.CallAsync(HttpMethod.Patch, $"/v1/users/{target.Id}",
				new { name = "PatchRenamed" });

			Assert.Equal(200, status);
		}
	}

	[Fact]
	public async Task Me_RedirectsWith307ToTheCallersOwnRoute_KeepingTheRest()
	{
		var (client, id) = await fixture.SignInAsync("MeRedirect");
		using (client)
		{
			using var response = await client.SendAsync(HttpMethod.Patch, "/v1/me/friends?x=1");

			Assert.Equal(307, (int)response.StatusCode);
			Assert.Equal($"/v1/users/{id}/friends?x=1", response.Headers.Location!.OriginalString);
		}
	}

	[Fact]
	public async Task SessionsAlias_RedirectsWith307()
	{
		var (client, id) = await fixture.SignInAsync("SessionsAlias");
		using (client)
		{
			using var response = await client.GetAsync("/v1/sessions");

			Assert.Equal(307, (int)response.StatusCode);
			Assert.Equal($"/v1/users/{id}/sessions", response.Headers.Location!.OriginalString);
		}
	}

	[Fact]
	public async Task Alias_WithoutAToken_Answers401()
	{
		using var client = new RestClient(fixture.Host.Url, fixture.Host.Domain);

		var (status, _) = await client.CallAsync(HttpMethod.Get, "/v1/me");

		Assert.Equal(401, status);
	}

	[Fact]
	public async Task Password_ChangeOfOwn_ClosesTheOtherTokensButNotTheCurrent()
	{
		var (first, id) = await fixture.SignInAsync("PwOwn");
		using var second = new RestClient(fixture.Host.Url, fixture.Host.Domain);
		await second.LoginAsync("PwOwn");
		using (first)
		{
			var (status, _) = await first.CallAsync(HttpMethod.Put, $"/v1/users/{id}/password",
				new { current = "password", @new = "changed" });
			var (current, _) = await first.CallAsync(HttpMethod.Get, "/v1/auth/session");
			var (other, _) = await second.CallAsync(HttpMethod.Get, "/v1/auth/session");

			Assert.Equal(200, status);
			Assert.Equal(200, current);
			Assert.Equal(401, other);
		}
	}

	[Fact]
	public async Task Password_WithTheWrongCurrent_Answers403WrongPassword()
	{
		var (client, id) = await fixture.SignInAsync("PwWrong");
		using (client)
		{
			var (status, envelope) = await client.CallAsync(HttpMethod.Put, $"/v1/users/{id}/password",
				new { current = "wrong", @new = "x" });

			Assert.Equal(403, status);
			Assert.Equal("wrongPassword", (string)envelope!["errors"]!["code"]!);
		}
	}

	[Fact]
	public async Task IrcKey_OfOwn_IsCreatedOnce_AndOfAnotherUserIsRefused()
	{
		var (client, id) = await fixture.SignInAsync("IrcKeyOwner");
		var other = await fixture.Host.CreateUserAsync("IrcKeyOther");
		using (client)
		{
			var (created, envelope) = await client.CallAsync(HttpMethod.Post, $"/v1/users/{id}/sessions/irc",
				new { label = "osu-ahr VPS" });
			var (refused, _) = await client.CallAsync(HttpMethod.Post, $"/v1/users/{other.Id}/sessions/irc",
				new { label = "x" });

			Assert.Equal(201, created);
			Assert.False(string.IsNullOrEmpty((string)envelope!["data"]!["key"]!));
			Assert.Equal(403, refused);
		}
	}

	[Fact]
	public async Task Sessions_ListOfAnotherUserWithoutPermission_Answers403_AndOwnIsListed()
	{
		var (client, id) = await fixture.SignInAsync("SessList");
		var other = await fixture.Host.CreateUserAsync("SessListOther");
		using (client)
		{
			var (own, envelope) = await client.CallAsync(HttpMethod.Get, $"/v1/users/{id}/sessions?state=online&types=8");
			var (foreign, _) = await client.CallAsync(HttpMethod.Get, $"/v1/users/{other.Id}/sessions");

			Assert.Equal(200, own);
			Assert.NotEmpty(envelope!["data"]!.AsArray());
			Assert.Equal(403, foreign);
		}
	}

	[Fact]
	public async Task Sessions_UnknownSessionId_Answers404()
	{
		var (client, id) = await fixture.SignInAsync("SessUnknown");
		using (client)
		{
			var (status, _) = await client.CallAsync(HttpMethod.Delete, $"/v1/users/{id}/sessions/{Guid.NewGuid()}");

			Assert.Equal(404, status);
		}
	}

	[Fact]
	public async Task Restriction_AddedByStaff_ShowsInTheUsersRestrictions_ThenLiftedAndDeleted()
	{
		var (staff, _) = await fixture.SignInAsync("RestrictStaff", RestHostFixture.StaffPermissions);
		var target = await fixture.Host.CreateUserAsync("RestrictTarget");
		using (staff)
		{
			var (created, envelope) = await staff.CallAsync(HttpMethod.Post, $"/v1/users/{target.Id}/restrictions",
				new { permissions = (ulong)UserPermissions.ChatPost, endsAt = (DateTimeOffset?)null, reason = "test" });
			var rid = (int)envelope!["data"]!["id"]!;
			var (history, list) = await staff.CallAsync(HttpMethod.Get, $"/v1/users/{target.Id}/restrictions");
			var (lifted, _) = await staff.CallAsync(HttpMethod.Post, $"/v1/users/{target.Id}/restrictions/{rid}/lift");
			var (deleted, _) = await staff.CallAsync(HttpMethod.Delete, $"/v1/users/{target.Id}/restrictions/{rid}");
			var (gone, empty) = await staff.CallAsync(HttpMethod.Get, $"/v1/users/{target.Id}/restrictions");

			Assert.Equal(201, created);
			Assert.Equal(200, history);
			Assert.Single(list!["data"]!.AsArray());
			Assert.Equal(200, lifted);
			Assert.Equal(200, deleted);
			Assert.Equal(200, gone);
			Assert.Empty(empty!["data"]!.AsArray());
		}
	}

	[Fact]
	public async Task Restriction_OfAnotherUserThroughThisUsersRoute_Answers404_AndIsLeftAlone()
	{
		var (staff, _) = await fixture.SignInAsync("IdorStaff", RestHostFixture.StaffPermissions);
		var a = await fixture.Host.CreateUserAsync("IdorA");
		var b = await fixture.Host.CreateUserAsync("IdorB");
		using (staff)
		{
			var (_, created) = await staff.CallAsync(HttpMethod.Post, $"/v1/users/{b.Id}/restrictions",
				new { permissions = (ulong)UserPermissions.ChatPost });
			var rid = (int)created!["data"]!["id"]!;

			var (lift, _) = await staff.CallAsync(HttpMethod.Post, $"/v1/users/{a.Id}/restrictions/{rid}/lift");
			var (delete, _) = await staff.CallAsync(HttpMethod.Delete, $"/v1/users/{a.Id}/restrictions/{rid}");
			var (_, user) = await staff.CallAsync(HttpMethod.Get, $"/v1/users/{b.Id}");
			var (_, history) = await staff.CallAsync(HttpMethod.Get, $"/v1/users/{b.Id}/restrictions");

			Assert.Equal(404, lift);
			Assert.Equal(404, delete);
			Assert.Single(user!["data"]!["restrictions"]!.AsArray());
			Assert.Null(history!["data"]![0]!["endsAt"]);
			Assert.Equal(rid, (int)history["data"]![0]!["id"]!);
		}
	}

	[Fact]
	public async Task Restriction_ByANonStaffUser_Answers403()
	{
		var (client, _) = await fixture.SignInAsync("RestrictNobody");
		var target = await fixture.Host.CreateUserAsync("RestrictVictim");
		using (client)
		{
			var (status, _) = await client.CallAsync(HttpMethod.Post, $"/v1/users/{target.Id}/restrictions",
				new { permissions = (ulong)UserPermissions.ChatPost });

			Assert.Equal(403, status);
		}
	}

	[Fact]
	public async Task Friends_AddedAndListed_OfOwnAccountOnly()
	{
		var (client, id) = await fixture.SignInAsync("FriendOwner");
		var friend = await fixture.Host.CreateUserAsync("FriendOther");
		using (client)
		{
			var (added, _) = await client.CallAsync(HttpMethod.Put, $"/v1/users/{id}/friends/{friend.Id}");
			var (list, envelope) = await client.CallAsync(HttpMethod.Get, $"/v1/users/{id}/friends");
			var (foreign, _) = await client.CallAsync(HttpMethod.Put, $"/v1/users/{friend.Id}/friends/{id}");

			Assert.Equal(200, added);
			Assert.Equal(200, list);
			Assert.Contains(envelope!["data"]!.AsArray(), item => (int)item!["id"]! == friend.Id);
			Assert.Equal(403, foreign);
		}
	}

	[Fact]
	public async Task Create_ByStaff_Answers201_AndByAnotherUserIsRefused()
	{
		var (staff, _) = await fixture.SignInAsync("CreateStaff", RestHostFixture.StaffPermissions);
		var (plain, _) = await fixture.SignInAsync("CreatePlain");
		using (staff)
		using (plain)
		{
			var (created, _) = await staff.CallAsync(HttpMethod.Post, "/v1/users",
				new { username = "CreatedByStaff", password = "pw" });
			var (refused, _) = await plain.CallAsync(HttpMethod.Post, "/v1/users",
				new { username = "CreatedByPlain", password = "pw" });

			Assert.Equal(201, created);
			Assert.Equal(403, refused);
		}
	}
}
