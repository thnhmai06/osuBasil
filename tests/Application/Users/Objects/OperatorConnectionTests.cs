using Basil.Application.Users.Objects;
using Basil.Domain.Authentication;
using Basil.Domain.Common;
using Basil.Domain.Cryptography;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Users.Tests.Objects;

/// <summary>
///     The console acts as BasilBot and is the one authority that skips the privilege checks; BasilBot's own connections
///     gain nothing from it.
/// </summary>
public sealed class OperatorConnectionTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Operate_ActsAsBot()
	{
		using var app = new TestApplication();

		var actor = await app.Users.OperateAsync(Token);

		Assert.Equal(BasilBot.Id, actor.Record.Id);
	}

	[Fact]
	public async Task KindApi_KeepsPrivileges()
	{
		using var app = new TestApplication();

		var actor = await app.Users.OperateAsync(Token);

		Assert.Equal(SessionType.Api, actor.Type);
		Assert.Equal(UserPrivilegesExtensions.All, actor.EffectivePrivileges(app.Clock.GetUtcNow()));
	}

	[Fact]
	public async Task BotOnline_NoConnection()
	{
		using var app = new TestApplication();
		await app.SeedBotAsync();
		var bot = await app.Users.ConnectBotAsync(Token);

		await app.Users.OperateAsync(Token);

		Assert.Equal(2, bot!.Connections.Count());
	}

	[Fact]
	public async Task BotOffline_NotOnline()
	{
		using var app = new TestApplication();

		await app.Users.OperateAsync(Token);

		Assert.Empty(app.Users.Online);
	}

	[Fact]
	public async Task Operator_CreatesEveryPrivilege()
	{
		using var app = new TestApplication();
		var actor = await app.Users.OperateAsync(Token);

		var (user, result) = await app.Users.ScopeTo(actor).CreateAccountAsync(
			new UserData { Name = "FirstAdmin" }, new Md5(new byte[16]), UserPrivilegesExtensions.All);

		Assert.True(result.IsOk);
		Assert.Equal(UserPrivilegesExtensions.All, user!.Value.Privileges);
	}

	[Fact]
	public async Task Operator_OutranksAnAdministrator()
	{
		using var app = new TestApplication();
		var admin = await app.CreateUserAsync("OpTargetAdmin", "password", UserPrivilegesExtensions.All);
		var actor = await app.Users.OperateAsync(Token);

		var result = await app.Users.Of(admin).ScopeTo(actor).Privileges.SetAsync(UserPrivileges.None);

		Assert.True(result.IsOk);
		Assert.Equal(UserPrivileges.None, admin.Value.Privileges);
	}

	[Fact]
	public async Task BotRecord_Refused()
	{
		using var app = new TestApplication();
		var actor = await app.Users.OperateAsync(Token);
		var bot = actor.Record;

		var result = await app.Users.Of(bot).ScopeTo(actor).Privileges.SetAsync(UserPrivilegesExtensions.All);

		Assert.Equal(UserOutcome.SystemAccount, result.Outcome);
	}

	[Fact]
	public async Task BasilBotConnections_NoAuthority()
	{
		using var app = new TestApplication();
		await app.Users.EnsureBotAsync(Token);
		var bot = await app.Users.ConnectBotAsync(Token);
		var target = await app.CreateUserAsync("BotTarget", "password");

		foreach (var connection in bot!.Connections)
		{
			var result = await app.Users.Of(target).ScopeTo(connection).Privileges
				.SetAsync(UserPrivilegesExtensions.All);

			Assert.False(result.IsOk);
		}
	}
}

/// <summary>What only the operator may do with relationships and profiles of other accounts.</summary>
public sealed class OperatorRelationshipTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task AwayMessage_AdminRefused()
	{
		using var app = new TestApplication();
		var target = await app.CreateUserAsync("AwayTarget", "password");
		var admin = await app.CreateUserAsync("AwayAdmin", "password", UserPrivilegesExtensions.All);
		var actor = await app.Users.OperateAsync(Token);
		var adminConnection = await app.LoginAsync(admin);

		Assert.True(await app.Users.Of(target).ScopeTo(actor).Profile.SetAwayAsync("back soon"));
		Assert.False(await app.Users.Of(target).ScopeTo(adminConnection).Profile.SetAwayAsync("back soon"));
	}
}