using Basil.Application.Users.Objects;
using Basil.Domain.Authentication;
using Basil.Domain.Common;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Users.Tests.Objects;

/// <summary>
///     The console acts as BasilBot and is the one authority that skips the rights checks; BasilBot's own connections
///     gain nothing from it.
/// </summary>
public sealed class OperatorConnectionTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task OperateAsync_ActsAsBasilBot()
	{
		using var app = new TestApplication();

		var actor = await app.Users.OperateAsync(Token);

		Assert.Equal(BasilBot.Id, actor.Record.Id);
	}

	[Fact]
	public async Task OperateAsync_KindIsApi_AndKeepsEveryConsolePermission()
	{
		using var app = new TestApplication();

		var actor = await app.Users.OperateAsync(Token);

		Assert.Equal(SessionType.Api, actor.Type);
		Assert.Equal(UserPermissionsGroups.All, actor.EffectivePermissions(app.Clock.GetUtcNow()));
	}

	[Fact]
	public async Task OperateAsync_BotOnline_DoesNotAddAConnectionToTheBot()
	{
		using var app = new TestApplication();
		await app.SeedBotAsync();
		var bot = await app.Users.ConnectBotAsync(Token);

		await app.Users.OperateAsync(Token);

		Assert.Equal(2, bot!.Connections.Count());
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
		var actor = await app.Users.OperateAsync(Token);

		var (user, result) = await app.Users.ScopeTo(actor).CreateAccountAsync(
			new UserData { Name = "FirstAdmin" }, new Md5(new byte[16]), UserPermissionsGroups.All,
			Token);

		Assert.True(result.IsOk);
		Assert.Equal(UserPermissionsGroups.All, user!.Value.Permissions);
	}

	[Fact]
	public async Task Operator_OutranksAnAdministrator()
	{
		using var app = new TestApplication();
		var admin = await app.CreateUserAsync("OpTargetAdmin", "password", UserPermissionsGroups.All);
		var actor = await app.Users.OperateAsync(Token);

		var result = await app.Users.Of(admin).ScopeTo(actor).Permissions.SetAsync(UserPermissions.None, Token);

		Assert.True(result.IsOk);
		Assert.Equal(UserPermissions.None, admin.Value.Permissions);
	}

	[Fact]
	public async Task Operator_CannotActOnBasilBotsRecord()
	{
		using var app = new TestApplication();
		var actor = await app.Users.OperateAsync(Token);
		var bot = actor.Record;

		var result = await app.Users.Of(bot).ScopeTo(actor).Permissions.SetAsync(UserPermissionsGroups.All, Token);

		Assert.Equal(UserOutcome.SystemAccount, result.Outcome);
	}

	[Fact]
	public async Task BasilBotsOwnConnections_StillHaveNoAuthority()
	{
		using var app = new TestApplication();
		await app.Users.EnsureBotAsync(Token);
		var bot = await app.Users.ConnectBotAsync(Token);
		var target = await app.CreateUserAsync("BotTarget", "password");

		foreach (var connection in bot!.Connections)
		{
			var result = await app.Users.Of(target).ScopeTo(connection).Permissions
				.SetAsync(UserPermissionsGroups.All, Token);

			Assert.False(result.IsOk);
		}
	}
}

/// <summary>What only the operator may do with relationships and profiles of other accounts.</summary>
public sealed class OperatorRelationshipTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task TheOperatorMaySetAnotherAccountsAwayMessage_ButAnAdministratorMayNot()
	{
		using var app = new TestApplication();
		var target = await app.CreateUserAsync("AwayTarget", "password");
		var admin = await app.CreateUserAsync("AwayAdmin", "password", UserPermissionsGroups.All);
		var actor = await app.Users.OperateAsync(Token);
		var adminConnection = await app.LoginAsync(admin);

		Assert.True(app.Users.Of(target).ScopeTo(actor).Profile.SetAway("back soon"));
		Assert.False(app.Users.Of(target).ScopeTo(adminConnection).Profile.SetAway("back soon"));
	}
}