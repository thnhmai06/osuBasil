using Basil.Application.Users.Objects;
using Basil.Domain.Common;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Users.Tests.Objects;

/// <summary>
///     The console acts as BasilBot and is the one authority that skips the rights checks; BasilBot's own connection
///     gains nothing from it.
/// </summary>
public sealed class OperatorConnectionTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task OperateAsync_ActsAsBasilBot()
	{
		using var app = new TestApplication();

		var by = await app.Users.OperateAsync(Token);

		Assert.Equal(BasilBot.Id, by.Record.Id);
	}

	[Fact]
	public async Task OperateAsync_BotOffline_IsNotRegisteredAsOnline()
	{
		using var app = new TestApplication();

		await app.Users.OperateAsync(Token);

		Assert.Empty(app.Users.Online);
	}

	[Fact]
	public async Task Operator_CreatesAnAccountWithEveryPermission()
	{
		using var app = new TestApplication();
		var by = await app.Users.OperateAsync(Token);

		var (user, result) = await app.Users.ScopeTo(by).CreateAccountAsync(
			new UserData { Name = "FirstAdmin", Permissions = UserPermissionsGroups.All }, new Md5(new byte[16]),
			Token);

		Assert.True(result.IsOk);
		Assert.Equal(UserPermissionsGroups.All, user!.Value.Permissions);
	}

	[Fact]
	public async Task Operator_OutranksAnAdministrator()
	{
		using var app = new TestApplication();
		var admin = await app.CreateUserAsync("OpTargetAdmin", "password", UserPermissionsGroups.All);
		var by = await app.Users.OperateAsync(Token);

		var result = await app.Users.Of(admin).ScopeTo(by).Permissions.SetAsync(UserPermissions.None, Token);

		Assert.True(result.IsOk);
		Assert.Equal(UserPermissions.None, admin.Value.Permissions);
	}

	[Fact]
	public async Task Operator_CannotActOnBasilBotsRecord()
	{
		using var app = new TestApplication();
		var by = await app.Users.OperateAsync(Token);
		var bot = by.Record;

		var result = await app.Users.Of(bot).ScopeTo(by).Permissions.SetAsync(UserPermissionsGroups.All, Token);

		Assert.Equal(UserOutcome.SystemAccount, result.Outcome);
	}

	[Fact]
	public async Task BasilBotsOwnConnection_StillHasNoAuthority()
	{
		using var app = new TestApplication();
		await app.Users.EnsureBotAsync(Token);
		var bot = await app.Users.ConnectBotAsync(Token);
		var target = await app.CreateUserAsync("BotTarget", "password");

		var result = await app.Users.Of(target).ScopeTo(bot!).Permissions.SetAsync(UserPermissionsGroups.All, Token);

		Assert.False(result.IsOk);
	}
}

/// <summary>What only the operator may do with relationships and profiles of other accounts.</summary>
public sealed class OperatorRelationshipTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task HeldByAsync_TheOperatorSetsAndRemovesAFriendForAnotherAccount()
	{
		using var app = new TestApplication();
		var owner = await app.CreateUserAsync("RelOwner", "password");
		var target = await app.CreateUserAsync("RelTarget", "password");
		var by = await app.Users.OperateAsync(Token);
		var scoped = app.Users.Of(target).ScopeTo(by);

		var set = await scoped.Relationships.HeldByAsync(owner, RelationshipType.Friend, true, Token);
		var (listResult, friends) = await app.Users.Of(owner).ScopeTo(by).Relationships.FriendsAsync(Token);
		var removed = await scoped.Relationships.HeldByAsync(owner, RelationshipType.Friend, false, Token);
		var (_, after) = await app.Users.Of(owner).ScopeTo(by).Relationships.FriendsAsync(Token);

		Assert.True(set.IsOk);
		Assert.True(listResult.IsOk);
		Assert.Contains(target, friends);
		Assert.True(removed.IsOk);
		Assert.DoesNotContain(target, after);
	}

	[Fact]
	public async Task HeldByAsync_AnyoneElseGetsNotSelf()
	{
		using var app = new TestApplication();
		var owner = await app.CreateUserAsync("RelOwner2", "password");
		var other = await app.CreateUserAsync("RelOther2", "password", UserPermissionsGroups.All);
		var target = await app.CreateUserAsync("RelTarget2", "password");
		var by = await app.LoginAsync(other);

		var result = await app.Users.Of(target).ScopeTo(by).Relationships
			.HeldByAsync(owner, RelationshipType.Friend, true, Token);

		Assert.Equal(UserOutcome.NotSelf, result.Outcome);
	}

	[Fact]
	public async Task HeldByAsync_RefusesTheSameAccountAndBasilBot()
	{
		using var app = new TestApplication();
		var owner = await app.CreateUserAsync("RelOwner3", "password");
		var by = await app.Users.OperateAsync(Token);

		Assert.Equal(UserOutcome.Self,
			(await app.Users.Of(owner).ScopeTo(by).Relationships
				.HeldByAsync(owner, RelationshipType.Block, true, Token)).Outcome);
		Assert.Equal(UserOutcome.SystemAccount,
			(await app.Users.Of(by.Record).ScopeTo(by).Relationships
				.HeldByAsync(owner, RelationshipType.Block, true, Token)).Outcome);
	}

	[Fact]
	public async Task TheOperatorMaySetAnotherAccountsAwayMessage_ButAnAdministratorMayNot()
	{
		using var app = new TestApplication();
		var target = await app.CreateUserAsync("AwayTarget", "password");
		var admin = await app.CreateUserAsync("AwayAdmin", "password", UserPermissionsGroups.All);
		var by = await app.Users.OperateAsync(Token);
		var adminConnection = await app.LoginAsync(admin);

		Assert.True(app.Users.Of(target).ScopeTo(by).Profile.SetAway("back soon"));
		Assert.False(app.Users.Of(target).ScopeTo(adminConnection).Profile.SetAway("back soon"));
	}
}