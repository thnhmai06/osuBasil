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
	public async Task PageBeyondEnd_Answers422(string path)
	{
		var (client, _) = await fixture.SignInAsync("Pg" + Interlocked.Increment(ref _pages));
		using (client)
		{
			var (status, _) = await client.CallAsync(HttpMethod.Get, path);

			Assert.Equal(422, status);
		}
	}

	[Fact]
	public async Task LargestPage_Answers200Paged()
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
	public async Task DeletedAccount_OnlyForViewers()
	{
		var (staff, _) = await fixture.SignInAsync("SearchDelStaff", RestHostFixture.StaffPrivileges);
		var (plain, _) = await fixture.SignInAsync("SearchDelPlain");
		var target = await fixture.Host.CreateUserAsync("SearchDelTarget");
		using (staff)
		using (plain)
		{
			await staff.CallAsync(HttpMethod.Delete, $"/v1/users/{target.Id}");

			var (_, seenByStaff) = await staff.CallAsync(HttpMethod.Get, "/v1/users?query=SearchDelTarget");
			var (_, seenByPlain) = await plain.CallAsync(HttpMethod.Get, "/v1/users?query=SearchDelTarget");

			Assert.Contains(seenByStaff!["data"]!.AsArray(), user => (int)user!["id"]! == target.Id);
			Assert.DoesNotContain(seenByPlain!["data"]!.AsArray(), user => (int)user!["id"]! == target.Id);
		}
	}

	[Fact]
	public async Task SearchWithoutToken_Answers401()
	{
		using var client = new RestClient(fixture.Host.Url, fixture.Host.Domain);

		var (status, _) = await client.CallAsync(HttpMethod.Get, "/v1/users");

		Assert.Equal(401, status);
	}

	[Fact]
	public async Task UnknownUser_Answers404()
	{
		using var client = new RestClient(fixture.Host.Url, fixture.Host.Domain);

		var (status, _) = await client.CallAsync(HttpMethod.Get, "/v1/users/999999");

		Assert.Equal(404, status);
	}

	[Fact]
	public async Task OwnName_Changes()
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
	public async Task OtherUserDenied_Answers403()
	{
		var (client, _) = await fixture.SignInAsync("PatchOther");
		var victim = await fixture.Host.CreateUserAsync("PatchVictim");
		using (client)
		{
			var (status, envelope) = await client.CallAsync(HttpMethod.Patch, $"/v1/users/{victim.Id}",
				new { name = "Stolen" });

			Assert.Equal(403, status);
			Assert.Equal("privilegeDenied", (string)envelope!["errors"]!["code"]!);
		}
	}

	[Fact]
	public async Task BotAsStaff_Answers409()
	{
		var (client, _) = await fixture.SignInAsync("PatchBotStaff", RestHostFixture.StaffPrivileges);
		using (client)
		{
			var (status, envelope) = await client.CallAsync(HttpMethod.Delete, "/v1/users/1");

			Assert.Equal(409, status);
			Assert.Equal("systemAccount", (string)envelope!["errors"]!["code"]!);
		}
	}

	[Fact]
	public async Task StaffRenamesOther_Succeeds()
	{
		var (client, _) = await fixture.SignInAsync("PatchStaff", RestHostFixture.StaffPrivileges);
		var target = await fixture.Host.CreateUserAsync("PatchTarget");
		using (client)
		{
			var (status, _) = await client.CallAsync(HttpMethod.Patch, $"/v1/users/{target.Id}",
				new { name = "PatchRenamed" });

			Assert.Equal(200, status);
		}
	}

	[Fact]
	public async Task MeAlias_Redirects307Kept()
	{
		var (client, id) = await fixture.SignInAsync("MeRedirect");
		using (client)
		{
			using var response = await client.SendAsync(HttpMethod.Patch, "/v1/me/friends?x=1",
				ct: TestContext.Current.CancellationToken);

			Assert.Equal(307, (int)response.StatusCode);
			Assert.Equal($"/v1/users/{id}/friends?x=1", response.Headers.Location!.OriginalString);
		}
	}

	[Fact]
	public async Task SessionsAlias_Redirects307()
	{
		var (client, id) = await fixture.SignInAsync("SessionsAlias");
		using (client)
		{
			using var response = await client.GetAsync("/v1/sessions", TestContext.Current.CancellationToken);

			Assert.Equal(307, (int)response.StatusCode);
			Assert.Equal($"/v1/users/{id}/sessions", response.Headers.Location!.OriginalString);
		}
	}

	[Fact]
	public async Task AliasWithoutToken_Answers401()
	{
		using var client = new RestClient(fixture.Host.Url, fixture.Host.Domain);

		var (status, _) = await client.CallAsync(HttpMethod.Get, "/v1/me");

		Assert.Equal(401, status);
	}

	[Fact]
	public async Task OwnPasswordChange_ClosesOtherTokens()
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
	public async Task WrongCurrentPassword_Answers403()
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
	public async Task OwnIrcKey_CreatedOthersRefused()
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
	public async Task IrcKeyEmpty_Answers422Json()
	{
		var (client, id) = await fixture.SignInAsync("IrcKeyNoBody");
		using (client)
		{
			var (status, envelope) = await client.CallAsync(HttpMethod.Post, $"/v1/users/{id}/sessions/irc");

			Assert.Equal(422, status);
			Assert.Equal("A JSON body is required.", (string)envelope!["message"]!);
		}
	}

	[Fact]
	public async Task OtherSessions_ForbiddenOwnListed()
	{
		var (client, id) = await fixture.SignInAsync("SessList");
		var other = await fixture.Host.CreateUserAsync("SessListOther");
		using (client)
		{
			var (own, envelope) =
				await client.CallAsync(HttpMethod.Get, $"/v1/users/{id}/sessions?state=online&types=8");
			var (foreign, _) = await client.CallAsync(HttpMethod.Get, $"/v1/users/{other.Id}/sessions");

			Assert.Equal(200, own);
			Assert.NotEmpty(envelope!["data"]!.AsArray());
			Assert.Equal(403, foreign);
		}
	}

	[Fact]
	public async Task UnknownSession_Answers404()
	{
		var (client, id) = await fixture.SignInAsync("SessUnknown");
		using (client)
		{
			var (status, _) = await client.CallAsync(HttpMethod.Delete, $"/v1/users/{id}/sessions/{long.MaxValue}");

			Assert.Equal(404, status);
		}
	}

	[Fact]
	public async Task StaffSilence_ShownThenLifted()
	{
		var (staff, _) = await fixture.SignInAsync("SilenceStaff", RestHostFixture.StaffPrivileges);
		var target = await fixture.Host.CreateUserAsync("SilenceTarget");
		using (staff)
		{
			var endsAt = fixture.Host.Time.GetUtcNow().AddHours(1);
			var (silenced, _) = await staff.CallAsync(HttpMethod.Put, $"/v1/users/{target.Id}/silence",
				new { endsAt, reason = "test" });
			var (_, whileSilenced) = await staff.CallAsync(HttpMethod.Get, $"/v1/users/{target.Id}");
			var (lifted, _) = await staff.CallAsync(HttpMethod.Delete, $"/v1/users/{target.Id}/silence");
			var (_, afterwards) = await staff.CallAsync(HttpMethod.Get, $"/v1/users/{target.Id}");

			Assert.Equal(200, silenced);
			Assert.Equal(endsAt, (DateTimeOffset)whileSilenced!["data"]!["silencedUntil"]!);
			Assert.Equal(200, lifted);
			Assert.Null(afterwards!["data"]!["silencedUntil"]);
		}
	}

	[Fact]
	public async Task NonModeratorSilence_Answers403()
	{
		var (client, _) = await fixture.SignInAsync("SilenceNobody");
		var target = await fixture.Host.CreateUserAsync("SilenceVictim");
		using (client)
		{
			var (status, _) = await client.CallAsync(HttpMethod.Put, $"/v1/users/{target.Id}/silence",
				new { endsAt = fixture.Host.Time.GetUtcNow().AddHours(1) });

			Assert.Equal(403, status);
		}
	}

	[Fact]
	public async Task Friends_OwnAccountOnly()
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
	public async Task CreateByStaff_SucceedsOthersRefused()
	{
		var (staff, _) = await fixture.SignInAsync("CreateStaff", RestHostFixture.StaffPrivileges);
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