using System.Net;
using System.Text;
using Basil.Application.Common.Settings;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Persistence;
using Basil.Domain.Authentication;
using Basil.Domain.Common;
using Basil.Domain.Cryptography;
using Basil.Domain.Moderation;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Users.Tests.Capabilities;

/// <summary>
///     Changing a password: a user's own change needs the current password and signs out every other session; staff
///     change another account's password under hierarchy and sign out everything; the area locked for profiles
///     refuses everyone who does not manage accounts.
/// </summary>
public sealed class ScopedUserCredentialsTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static Md5 Hash(string password)
	{
		return new Md5(Encoding.UTF8.GetBytes(password));
	}

	private static Task<LoginResult> OpenWithTokenAsync(TestApplication app, SessionType type, string token)
	{
		return app.Users.LoginWithTokenAsync(type, token, IPAddress.Loopback, ct: Token);
	}

	private static async Task<string> OpenIrcKeyAsync(TestApplication app, Connection actor)
	{
		var (result, key, _) = await app.Users.Of(actor.Record).ScopeTo(actor).Sessions.OpenIrcAsync("client");
		Assert.True(result.IsOk);
		return key!;
	}

	[Fact]
	public async Task OwnAccount_SignsOutOthers()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Changer", "old");
		var current = await app.LoginAsync(user, SessionType.Api, "old");
		var otherApi = await app.LoginAsync(user, SessionType.Api, "old");
		var osu = await app.LoginAsync(user, SessionType.Stable, "old");
		var key = await OpenIrcKeyAsync(app, current);

		var result = await app.Users.Of(user).ScopeTo(current).Credentials
			.ChangePasswordAsync(Hash("new"), Hash("old"), false);

		Assert.True(result.IsOk);
		Assert.True(await app.VerifiesPasswordAsync(user, "new"));
		Assert.False(await app.VerifiesPasswordAsync(user, "old"));
		Assert.True(current.IsOpen);
		Assert.False(otherApi.IsOpen);
		Assert.False(osu.IsOpen);
		Assert.Equal(LoginFailure.InvalidToken,
			(await OpenWithTokenAsync(app, SessionType.Api, otherApi.Token)).Failure);
		Assert.Equal(LoginFailure.InvalidToken, (await OpenWithTokenAsync(app, SessionType.Irc, key)).Failure);
		Assert.True((await OpenWithTokenAsync(app, SessionType.Api, current.Token)).Succeeded);
	}

	[Fact]
	public async Task IncludeCurrent_SignsOutCaller()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Everything", "old");
		var current = await app.LoginAsync(user, SessionType.Api, "old");

		await app.Users.Of(user).ScopeTo(current).Credentials
			.ChangePasswordAsync(Hash("new"), Hash("old"), true);

		Assert.False(current.IsOpen);
		Assert.Equal(LoginFailure.InvalidToken,
			(await OpenWithTokenAsync(app, SessionType.Api, current.Token)).Failure);
	}

	[Fact]
	public async Task SamePassword_SignsNobodyOut()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Same", "old");
		var current = await app.LoginAsync(user, SessionType.Api, "old");
		var other = await app.LoginAsync(user, SessionType.Api, "old");

		var result = await app.Users.Of(user).ScopeTo(current).Credentials
			.ChangePasswordAsync(Hash("old"), Hash("old"), true);

		Assert.True(result.IsOk);
		Assert.True(current.IsOpen);
		Assert.True(other.IsOpen);
	}

	[Fact]
	public async Task OwnWrongCurrent_NothingChanged()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Wrong", "old");
		var current = await app.LoginAsync(user, SessionType.Api, "old");
		var other = await app.LoginAsync(user, SessionType.Api, "old");

		var result = await app.Users.Of(user).ScopeTo(current).Credentials
			.ChangePasswordAsync(Hash("new"), Hash("guess"), false);

		Assert.Equal(UserOutcome.WrongPassword, result.Outcome);
		Assert.True(await app.VerifiesPasswordAsync(user, "old"));
		Assert.True(other.IsOpen);
	}

	[Fact]
	public async Task OwnNoCurrent_NothingChanged()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Missing", "old");
		var current = await app.LoginAsync(user, SessionType.Api, "old");

		var result = await app.Users.Of(user).ScopeTo(current).Credentials.ChangePasswordAsync(Hash("new"), null);

		Assert.Equal(UserOutcome.WrongPassword, result.Outcome);
		Assert.True(await app.VerifiesPasswordAsync(user, "old"));
	}

	[Fact]
	public async Task StaffNoCurrent_SignsOutAll()
	{
		using var app = new TestApplication();
		var admin = await app.LoginAsync(await app.CreateUserAsync("Staff", "password", UserPrivilegesExtensions.All));
		var user = await app.CreateUserAsync("Reset", "old");
		var api = await app.LoginAsync(user, SessionType.Api, "old");
		var osu = await app.LoginAsync(user, SessionType.Stable, "old");
		var key = await OpenIrcKeyAsync(app, api);

		var result = await app.Users.Of(user).ScopeTo(admin).Credentials.ChangePasswordAsync(Hash("new"), null);

		Assert.True(result.IsOk);
		Assert.True(await app.VerifiesPasswordAsync(user, "new"));
		Assert.False(api.IsOpen);
		Assert.False(osu.IsOpen);
		Assert.Equal(LoginFailure.InvalidToken, (await OpenWithTokenAsync(app, SessionType.Irc, key)).Failure);
		var entry = Assert.Single(app.ModerationEntries, e => e.Action == ModerationAction.ChangePassword);
		Assert.Equal(admin.Record, entry.Actor);
		Assert.Equal(user, entry.Target);
	}

	[Fact]
	public async Task UserWithoutManage_NamesPrivilege()
	{
		using var app = new TestApplication();
		var other = await app.LoginAsync(await app.CreateUserAsync("Nosy", "password"));
		var user = await app.CreateUserAsync("Victim", "old");

		var result = await app.Users.Of(user).ScopeTo(other).Credentials.ChangePasswordAsync(Hash("new"), null);

		Assert.Equal(UserPrivileges.Owner, result.Missing);
		Assert.True(await app.VerifiesPasswordAsync(user, "old"));
	}

	[Fact]
	public async Task EqualRankStaff_Refused()
	{
		using var app = new TestApplication();
		var admin = await app.LoginAsync(await app.CreateUserAsync("Admin", "password", UserPrivilegesExtensions.All));
		var peer = await app.CreateUserAsync("Peer", "old", UserPrivilegesExtensions.All);

		var result = await app.Users.Of(peer).ScopeTo(admin).Credentials.ChangePasswordAsync(Hash("new"), null);

		Assert.Equal(UserOutcome.NotOutranking, result.Outcome);
		Assert.True(await app.VerifiesPasswordAsync(peer, "old"));
	}

	[Fact]
	public async Task LockedProfiles_NonManagerRefused()
	{
		using var app = new TestApplication();
		await app.SetSettingsAsync(new ServerSettings { Locks = Locks.Profiles });
		var user = await app.CreateUserAsync("Locked", "old");
		var current = await app.LoginAsync(user, SessionType.Api, "old");

		var result = await app.Users.Of(user).ScopeTo(current).Credentials
			.ChangePasswordAsync(Hash("new"), Hash("old"), false);

		Assert.Equal(UserOutcome.Locked, result.Outcome);
		Assert.True(await app.VerifiesPasswordAsync(user, "old"));
	}

	[Fact]
	public async Task LockedProfiles_ManagerChangesOwn()
	{
		using var app = new TestApplication();
		await app.SetSettingsAsync(new ServerSettings { Locks = Locks.Profiles });
		var user = await app.CreateUserAsync("Manager", "old",
			UserPrivilegesExtensions.Default | UserPrivileges.Owner);
		var current = await app.LoginAsync(user, SessionType.Api, "old");

		var result = await app.Users.Of(user).ScopeTo(current).Credentials
			.ChangePasswordAsync(Hash("new"), Hash("old"), false);

		Assert.True(result.IsOk);
		Assert.True(await app.VerifiesPasswordAsync(user, "new"));
	}

	[Fact]
	public async Task BasilBotPassword_RefusedForAll()
	{
		using var app = new TestApplication();
		var admin = await app.LoginAsync(await app.CreateUserAsync("Staff", "password", UserPrivilegesExtensions.All));
		var bot = await app.SeedBotAsync();

		var result = await app.Users.Of(bot).ScopeTo(admin).Credentials.ChangePasswordAsync(Hash("new"), null);

		Assert.Equal(UserOutcome.SystemAccount, result.Outcome);
		Assert.True(await app.VerifiesPasswordAsync(bot, "basil-bot"));
	}
}