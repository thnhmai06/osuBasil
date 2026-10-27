using Basil.Application.Common;
using Basil.Application.Users.Persistence;
using Basil.Domain.Users;
using Basil.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Users.Tests.Users;

/// <summary>
///     The account of id <see cref="BasilBot.Id" /> is BasilBot whatever it held before: another account is taken
///     over, a deleted one is restored and a missing one is created, with no permissions, no password and no
///     restriction.
/// </summary>
public sealed class BotAccountTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task EnsureBotAsync_AccountHoldsTheId_KeepsItsNameAndCountry()
	{
		using var app = new TestApplication();
		var other = await app.SeedAccountAsync(BasilBot.Id, "Somebody", "secret");
		other.Value.Country = Country.Au;

		var bot = await app.Users.EnsureBotAsync(Token);

		Assert.Equal(BasilBot.Id, bot.Id);
		Assert.Equal("Somebody", bot.Value.Name);
		Assert.Equal(Country.Au, bot.Value.Country);
	}

	[Fact]
	public async Task EnsureBotAsync_AccountHoldsTheId_RemovesItsPermissions()
	{
		using var app = new TestApplication();
		await app.SeedAccountAsync(BasilBot.Id, "Somebody", "secret", UserPermissions.RoomManageAny);

		var bot = await app.Users.EnsureBotAsync(Token);

		Assert.Equal(UserPermissions.None, bot.Value.Permissions);
		Assert.Equal(UserPermissions.None, (await StoredBotAsync(app)).Value.Permissions);
	}

	[Fact]
	public async Task EnsureBotAsync_AccountHoldsTheId_RemovesItsPassword()
	{
		using var app = new TestApplication();
		var other = await app.SeedAccountAsync(BasilBot.Id, "Somebody", "secret");
		Assert.True(await app.VerifiesPasswordAsync(other, "secret"));

		var bot = await app.Users.EnsureBotAsync(Token);

		Assert.False(await app.VerifiesPasswordAsync(bot, "secret"));
	}

	[Fact]
	public async Task EnsureBotAsync_AccountHasActiveRestrictions_EndsThem()
	{
		using var app = new TestApplication();
		var other = await app.SeedAccountAsync(BasilBot.Id, "Somebody", "secret");
		app.AddRestriction(other, RestrictionData.Silence);
		app.AddRestriction(other, RestrictionData.Restricted, app.Clock.GetUtcNow().AddDays(7));

		var bot = await app.Users.EnsureBotAsync(Token);

		Assert.Empty(await ActiveRestrictionsAsync(app, bot));
	}

	[Fact]
	public async Task EnsureBotAsync_AccountHasRestrictionNotStartedYet_NeverApplies()
	{
		using var app = new TestApplication();
		var other = await app.SeedAccountAsync(BasilBot.Id, "Somebody", "secret");
		var startsAt = app.Clock.GetUtcNow().AddDays(1);
		app.AddRestriction(other, RestrictionData.Silence, startsAt: startsAt);

		var bot = await app.Users.EnsureBotAsync(Token);
		app.Clock.Advance(TimeSpan.FromDays(2));

		Assert.Empty(await ActiveRestrictionsAsync(app, bot));
	}

	[Fact]
	public async Task EnsureBotAsync_AccountHasEndedRestriction_KeepsItInTheHistory()
	{
		using var app = new TestApplication();
		var other = await app.SeedAccountAsync(BasilBot.Id, "Somebody", "secret");
		var endedAt = app.Clock.GetUtcNow().AddMinutes(-1);
		var ended = app.AddRestriction(other, RestrictionData.Silence, endedAt, endedAt.AddHours(-1));

		var bot = await app.Users.EnsureBotAsync(Token);

		var history = await app.Provider.GetRequiredService<IRestrictionRepository>().ListAsync(bot, Token);
		Assert.Equal(endedAt, Assert.Single(history, restriction => restriction.Id == ended.Id).Value.EndsAt);
	}

	[Fact]
	public async Task EnsureBotAsync_IdHoldsDeletedAccount_RestoresItWithTheDefaultName()
	{
		using var app = new TestApplication();
		await app.SeedAccountAsync(BasilBot.Id, "Gone", "secret", deletedAt: app.Clock.GetUtcNow());

		var bot = await app.Users.EnsureBotAsync(Token);

		Assert.Null(bot.Value.DeletedAt);
		Assert.Equal(BasilBot.DefaultName, bot.Value.Name);
		Assert.Equal(UserPermissions.None, bot.Value.Permissions);
		Assert.Null((await StoredBotAsync(app)).Value.DeletedAt);
	}

	[Fact]
	public async Task EnsureBotAsync_IdHoldsDeletedAccount_RemovesItsPassword()
	{
		using var app = new TestApplication();
		await app.SeedAccountAsync(BasilBot.Id, "Gone", "secret", deletedAt: app.Clock.GetUtcNow());

		var bot = await app.Users.EnsureBotAsync(Token);

		Assert.False(await app.VerifiesPasswordAsync(bot, "secret"));
	}

	[Fact]
	public async Task EnsureBotAsync_DeletedAccountAndDefaultNameTaken_Throws()
	{
		using var app = new TestApplication();
		await app.SeedAccountAsync(BasilBot.Id, "Gone", "secret", deletedAt: app.Clock.GetUtcNow());
		await app.CreateUserAsync(BasilBot.DefaultName, "password");

		await Assert.ThrowsAsync<AlreadyExistsException>(() => app.Users.EnsureBotAsync(Token));
	}

	[Fact]
	public async Task EnsureBotAsync_IdHoldsNothing_CreatesTheBotWithTheFixedId()
	{
		using var app = new TestApplication();

		var bot = await app.Users.EnsureBotAsync(Token);

		Assert.Equal(BasilBot.Id, bot.Id);
		Assert.Equal(BasilBot.DefaultName, bot.Value.Name);
		Assert.Equal(UserPermissions.None, bot.Value.Permissions);
		Assert.Null(bot.Value.DeletedAt);
		Assert.Same(bot, await StoredBotAsync(app));
	}

	[Fact]
	public async Task EnsureBotAsync_IdHoldsNothing_AccountsCreatedLaterTakeOtherIds()
	{
		using var app = new TestApplication();
		await app.Users.EnsureBotAsync(Token);

		var next = await app.CreateUserAsync("NextPlayer", "password");

		Assert.NotEqual(BasilBot.Id, next.Id);
	}

	[Fact]
	public async Task EnsureBotAsync_AfterTakeover_BotCanComeOnline()
	{
		using var app = new TestApplication();
		await app.SeedAccountAsync(BasilBot.Id, "Somebody", "secret", UserPermissions.RoomManageAny);

		var bot = await app.Users.EnsureBotAsync(Token);
		var connection = await app.Users.ConnectBotAsync(Token);

		Assert.NotNull(connection);
		Assert.True(connection.IsOpen);
		Assert.Contains(bot, app.Users.Online.Keys);
	}

	[Fact]
	public async Task EnsureBotAsync_AfterTakeover_NobodyCanSignInAsTheBot()
	{
		using var app = new TestApplication();
		await app.SeedAccountAsync(BasilBot.Id, "Somebody", "secret");
		await app.Users.EnsureBotAsync(Token);

		var result = await app.AttemptLoginAsync("Somebody", "secret");

		Assert.Equal(LoginFailure.UnknownUser, result.Failure);
	}

	[Fact]
	public async Task EnsureBotAsync_CalledTwice_ChangesNothing()
	{
		using var app = new TestApplication();
		await app.SeedAccountAsync(BasilBot.Id, "Somebody", "secret", UserPermissions.RoomManageAny);
		var first = await app.Users.EnsureBotAsync(Token);

		var second = await app.Users.EnsureBotAsync(Token);

		Assert.Same(first, second);
		Assert.Equal("Somebody", second.Value.Name);
		Assert.Equal(UserPermissions.None, second.Value.Permissions);
		Assert.Null(second.Value.DeletedAt);
	}

	[Fact]
	public async Task EnsureBotAsync_BotIsOnline_KeepsItsConnectionOpen()
	{
		using var app = new TestApplication();
		await app.Users.EnsureBotAsync(Token);
		var connection = await app.Users.ConnectBotAsync(Token);

		await app.Users.EnsureBotAsync(Token);

		Assert.NotNull(connection);
		Assert.True(connection.IsOpen);
		Assert.Same(connection, await app.Users.ConnectBotAsync(Token));
	}

	[Fact]
	public async Task EnsureBotAsync_BotWasGivenPermissionsWhileOnline_ResetsItAndClosesItsConnection()
	{
		using var app = new TestApplication();
		var bot = await app.Users.EnsureBotAsync(Token);
		var connection = await app.Users.ConnectBotAsync(Token);
		bot.Value.Permissions = UserPermissions.RoomManageAny;

		var restored = await app.Users.EnsureBotAsync(Token);

		Assert.NotNull(connection);
		Assert.False(connection.IsOpen);
		Assert.Equal(UserPermissions.None, restored.Value.Permissions);
	}

	private static async Task<UserRecord> StoredBotAsync(TestApplication app)
	{
		return await app.Provider.GetRequiredService<IUserRepository>().GetAsync(BasilBot.Id, Token)
		       ?? throw new InvalidOperationException("The bot is not stored.");
	}

	private static async Task<List<UserRestriction>> ActiveRestrictionsAsync(TestApplication app, UserRecord user)
	{
		var now = app.Clock.GetUtcNow();
		return (await app.Provider.GetRequiredService<IRestrictionRepository>().ListAsync(user, Token))
			.Where(restriction => restriction.Value.IsActive(now)).ToList();
	}
}