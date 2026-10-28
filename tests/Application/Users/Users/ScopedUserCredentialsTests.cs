using System.Net;
using System.Text;
using Basil.Application.Content.Persistence;
using Basil.Application.Users.Connections;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Users;
using Basil.Domain.Auth;
using Basil.Domain.Common;
using Basil.Domain.Moderation;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Users.Tests.Users;

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

	private static Task<LoginResult> OpenWithTokenAsync(TestApplication app, ConnectionType type, string token)
	{
		return app.Users.LoginWithTokenAsync(type, token, IPAddress.Loopback, cancellationToken: Token);
	}

	private static async Task<string> OpenIrcKeyAsync(TestApplication app, Connection by)
	{
		var (result, key, _) = await app.Users.Of(by.Record).ScopeTo(by).Sessions.OpenIrcAsync("client", Token);
		Assert.True(result.IsOk);
		return key!;
	}

	[Fact]
	public async Task ChangePasswordAsync_OfOwnAccount_ReplacesThePasswordAndSignsOutEveryOtherSession()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Changer", "old");
		var current = await app.LoginAsync(user, ConnectionType.Api, "old");
		var otherApi = await app.LoginAsync(user, ConnectionType.Api, "old");
		var osu = await app.LoginAsync(user, ConnectionType.Osu, "old");
		var key = await OpenIrcKeyAsync(app, current);

		var result = await app.Users.Of(user).ScopeTo(current).Credentials
			.ChangePasswordAsync(Hash("new"), Hash("old"), false, Token);

		Assert.True(result.IsOk);
		Assert.True(await app.VerifiesPasswordAsync(user, "new"));
		Assert.False(await app.VerifiesPasswordAsync(user, "old"));
		Assert.True(current.IsOpen);
		Assert.False(otherApi.IsOpen);
		Assert.False(osu.IsOpen);
		Assert.Equal(LoginFailure.InvalidToken, (await OpenWithTokenAsync(app, ConnectionType.Api, otherApi.Token)).Failure);
		Assert.Equal(LoginFailure.InvalidToken, (await OpenWithTokenAsync(app, ConnectionType.Irc, key)).Failure);
		Assert.True((await OpenWithTokenAsync(app, ConnectionType.Api, current.Token)).Succeeded);
	}

	[Fact]
	public async Task ChangePasswordAsync_WithIncludeCurrent_SignsOutTheCallingSessionToo()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Everything", "old");
		var current = await app.LoginAsync(user, ConnectionType.Api, "old");

		await app.Users.Of(user).ScopeTo(current).Credentials.ChangePasswordAsync(Hash("new"), Hash("old"), true, Token);

		Assert.False(current.IsOpen);
		Assert.Equal(LoginFailure.InvalidToken, (await OpenWithTokenAsync(app, ConnectionType.Api, current.Token)).Failure);
	}

	[Fact]
	public async Task ChangePasswordAsync_ToTheSamePassword_SignsNobodyOut()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Same", "old");
		var current = await app.LoginAsync(user, ConnectionType.Api, "old");
		var other = await app.LoginAsync(user, ConnectionType.Api, "old");

		var result = await app.Users.Of(user).ScopeTo(current).Credentials
			.ChangePasswordAsync(Hash("old"), Hash("old"), true, Token);

		Assert.True(result.IsOk);
		Assert.True(current.IsOpen);
		Assert.True(other.IsOpen);
	}

	[Fact]
	public async Task ChangePasswordAsync_OfOwnAccountWithAWrongCurrentPassword_ChangesNothing()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Wrong", "old");
		var current = await app.LoginAsync(user, ConnectionType.Api, "old");
		var other = await app.LoginAsync(user, ConnectionType.Api, "old");

		var result = await app.Users.Of(user).ScopeTo(current).Credentials
			.ChangePasswordAsync(Hash("new"), Hash("guess"), false, Token);

		Assert.Equal(UserOutcome.WrongPassword, result.Outcome);
		Assert.True(await app.VerifiesPasswordAsync(user, "old"));
		Assert.True(other.IsOpen);
	}

	[Fact]
	public async Task ChangePasswordAsync_OfOwnAccountWithoutTheCurrentPassword_ChangesNothing()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Missing", "old");
		var current = await app.LoginAsync(user, ConnectionType.Api, "old");

		var result = await app.Users.Of(user).ScopeTo(current).Credentials.ChangePasswordAsync(Hash("new"), null,
			cancellationToken: Token);

		Assert.Equal(UserOutcome.WrongPassword, result.Outcome);
		Assert.True(await app.VerifiesPasswordAsync(user, "old"));
	}

	[Fact]
	public async Task ChangePasswordAsync_ByStaff_NeedsNoCurrentPassword_SignsOutEverythingAndRecordsIt()
	{
		using var app = new TestApplication();
		var admin = await app.LoginAsync(await app.CreateUserAsync("Staff", "password", UserPermissionsGroups.All));
		var user = await app.CreateUserAsync("Reset", "old");
		var api = await app.LoginAsync(user, ConnectionType.Api, "old");
		var osu = await app.LoginAsync(user, ConnectionType.Osu, "old");
		var key = await OpenIrcKeyAsync(app, api);

		var result = await app.Users.Of(user).ScopeTo(admin).Credentials.ChangePasswordAsync(Hash("new"), null,
			cancellationToken: Token);

		Assert.True(result.IsOk);
		Assert.True(await app.VerifiesPasswordAsync(user, "new"));
		Assert.False(api.IsOpen);
		Assert.False(osu.IsOpen);
		Assert.Equal(LoginFailure.InvalidToken, (await OpenWithTokenAsync(app, ConnectionType.Irc, key)).Failure);
		var entry = Assert.Single(app.ModerationEntries, e => e.Action == ModerationAction.ChangePassword);
		Assert.Equal(admin.Record, entry.By);
		Assert.Equal(user, entry.Target);
	}

	[Fact]
	public async Task ChangePasswordAsync_ByAUserWithoutUserManage_NamesTheMissingPermission()
	{
		using var app = new TestApplication();
		var other = await app.LoginAsync(await app.CreateUserAsync("Nosy", "password"));
		var user = await app.CreateUserAsync("Victim", "old");

		var result = await app.Users.Of(user).ScopeTo(other).Credentials.ChangePasswordAsync(Hash("new"), null,
			cancellationToken: Token);

		Assert.Equal(UserPermissions.UserManage, result.Missing);
		Assert.True(await app.VerifiesPasswordAsync(user, "old"));
	}

	[Fact]
	public async Task ChangePasswordAsync_ByStaffOfEqualRank_IsRefused()
	{
		using var app = new TestApplication();
		var admin = await app.LoginAsync(await app.CreateUserAsync("Admin", "password", UserPermissionsGroups.All));
		var peer = await app.CreateUserAsync("Peer", "old", UserPermissionsGroups.All);

		var result = await app.Users.Of(peer).ScopeTo(admin).Credentials.ChangePasswordAsync(Hash("new"), null,
			cancellationToken: Token);

		Assert.Equal(UserOutcome.NotOutranking, result.Outcome);
		Assert.True(await app.VerifiesPasswordAsync(peer, "old"));
	}

	[Fact]
	public async Task ChangePasswordAsync_WhileProfilesAreLocked_RefusesAUserWhoDoesNotManageAccounts()
	{
		using var app = new TestApplication();
		await app.SetSettingsAsync(new ServerSettings { Locks = Locks.Profiles });
		var user = await app.CreateUserAsync("Locked", "old");
		var current = await app.LoginAsync(user, ConnectionType.Api, "old");

		var result = await app.Users.Of(user).ScopeTo(current).Credentials
			.ChangePasswordAsync(Hash("new"), Hash("old"), false, Token);

		Assert.Equal(UserOutcome.Locked, result.Outcome);
		Assert.True(await app.VerifiesPasswordAsync(user, "old"));
	}

	[Fact]
	public async Task ChangePasswordAsync_WhileProfilesAreLocked_LetsAnAccountManagerChangeTheirOwn()
	{
		using var app = new TestApplication();
		await app.SetSettingsAsync(new ServerSettings { Locks = Locks.Profiles });
		var user = await app.CreateUserAsync("Manager", "old", UserPermissionsGroups.Default | UserPermissions.UserManage);
		var current = await app.LoginAsync(user, ConnectionType.Api, "old");

		var result = await app.Users.Of(user).ScopeTo(current).Credentials
			.ChangePasswordAsync(Hash("new"), Hash("old"), false, Token);

		Assert.True(result.IsOk);
		Assert.True(await app.VerifiesPasswordAsync(user, "new"));
	}

	[Fact]
	public async Task ChangePasswordAsync_OfBasilBot_IsRefusedForEveryone()
	{
		using var app = new TestApplication();
		var admin = await app.LoginAsync(await app.CreateUserAsync("Staff", "password", UserPermissionsGroups.All));
		var bot = await app.SeedBotAsync();

		var result = await app.Users.Of(bot).ScopeTo(admin).Credentials.ChangePasswordAsync(Hash("new"), null,
			cancellationToken: Token);

		Assert.Equal(UserOutcome.SystemAccount, result.Outcome);
		Assert.True(await app.VerifiesPasswordAsync(bot, "basil-bot"));
	}
}
