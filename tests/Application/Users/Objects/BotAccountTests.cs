using Basil.Application.Shared;
using Basil.Application.Users.Persistence;
using Basil.Domain.Users;
using Basil.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Users.Tests.Objects;

/// <summary>
///     The account of id <see cref="BasilBot.Id" /> is BasilBot whatever it held before: another account is taken
///     over, a deleted one is restored and a missing one is created, with no privileges, no password and no
///     restriction.
/// </summary>
public sealed class BotAccountTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task ExistingBot_KeepsNameCountry()
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
	public async Task ExistingBot_RemovesPrivileges()
	{
		using var app = new TestApplication();
		await app.SeedAccountAsync(BasilBot.Id, "Somebody", "secret", UserPrivileges.Tournament);

		var bot = await app.Users.EnsureBotAsync(Token);

		Assert.Equal(UserPrivileges.None, bot.Value.Privileges);
		Assert.Equal(UserPrivileges.None, (await StoredBotAsync(app)).Value.Privileges);
	}

	[Fact]
	public async Task ExistingBot_RemovesPassword()
	{
		using var app = new TestApplication();
		var other = await app.SeedAccountAsync(BasilBot.Id, "Somebody", "secret");
		Assert.True(await app.VerifiesPasswordAsync(other, "secret"));

		var bot = await app.Users.EnsureBotAsync(Token);

		Assert.False(await app.VerifiesPasswordAsync(bot, "secret"));
	}

	[Fact]
	public async Task DeletedBotAccount_RestoredDefaultName()
	{
		using var app = new TestApplication();
		await app.SeedAccountAsync(BasilBot.Id, "Gone", "secret", deletedAt: app.Clock.GetUtcNow());

		var bot = await app.Users.EnsureBotAsync(Token);

		Assert.Null(bot.Value.DeletedAt);
		Assert.Equal(BasilBot.DefaultName, bot.Value.Name);
		Assert.Equal(UserPrivileges.None, bot.Value.Privileges);
		Assert.Null((await StoredBotAsync(app)).Value.DeletedAt);
	}

	[Fact]
	public async Task DeletedBotAccount_RemovesPassword()
	{
		using var app = new TestApplication();
		await app.SeedAccountAsync(BasilBot.Id, "Gone", "secret", deletedAt: app.Clock.GetUtcNow());

		var bot = await app.Users.EnsureBotAsync(Token);

		Assert.False(await app.VerifiesPasswordAsync(bot, "secret"));
	}

	[Fact]
	public async Task DefaultNameTaken_Throws()
	{
		using var app = new TestApplication();
		await app.SeedAccountAsync(BasilBot.Id, "Gone", "secret", deletedAt: app.Clock.GetUtcNow());
		await app.CreateUserAsync(BasilBot.DefaultName, "password");

		await Assert.ThrowsAsync<AlreadyExistsException>(() => app.Users.EnsureBotAsync(Token));
	}

	[Fact]
	public async Task MissingBot_CreatesFixedId()
	{
		using var app = new TestApplication();

		var bot = await app.Users.EnsureBotAsync(Token);

		Assert.Equal(BasilBot.Id, bot.Id);
		Assert.Equal(BasilBot.DefaultName, bot.Value.Name);
		Assert.Equal(UserPrivileges.None, bot.Value.Privileges);
		Assert.Null(bot.Value.DeletedAt);
		Assert.Same(bot, await StoredBotAsync(app));
	}

	[Fact]
	public async Task LaterAccounts_AvoidFixedId()
	{
		using var app = new TestApplication();
		await app.Users.EnsureBotAsync(Token);

		var next = await app.CreateUserAsync("NextPlayer", "password");

		Assert.NotEqual(BasilBot.Id, next.Id);
	}

	[Fact]
	public async Task AfterTakeover_BotComesOnline()
	{
		using var app = new TestApplication();
		await app.SeedAccountAsync(BasilBot.Id, "Somebody", "secret", UserPrivileges.Tournament);

		var bot = await app.Users.EnsureBotAsync(Token);
		var online = await app.Users.ConnectBotAsync(Token);

		Assert.NotNull(online);
		Assert.All(online.Connections, connection => Assert.True(connection.IsOpen));
		Assert.Contains(bot, app.Users.Online.Keys);
	}

	[Fact]
	public async Task AfterTakeover_SignInRefused()
	{
		using var app = new TestApplication();
		await app.SeedAccountAsync(BasilBot.Id, "Somebody", "secret");
		await app.Users.EnsureBotAsync(Token);

		var result = await app.AttemptLoginAsync("Somebody", "secret");

		Assert.Equal(LoginFailure.UnknownUser, result.Failure);
	}

	[Fact]
	public async Task SecondCall_ChangesNothing()
	{
		using var app = new TestApplication();
		await app.SeedAccountAsync(BasilBot.Id, "Somebody", "secret", UserPrivileges.Tournament);
		var first = await app.Users.EnsureBotAsync(Token);

		var second = await app.Users.EnsureBotAsync(Token);

		Assert.Same(first, second);
		Assert.Equal("Somebody", second.Value.Name);
		Assert.Equal(UserPrivileges.None, second.Value.Privileges);
		Assert.Null(second.Value.DeletedAt);
	}

	[Fact]
	public async Task OnlineBot_KeepsConnections()
	{
		using var app = new TestApplication();
		await app.Users.EnsureBotAsync(Token);
		var online = await app.Users.ConnectBotAsync(Token);

		await app.Users.EnsureBotAsync(Token);

		Assert.NotNull(online);
		Assert.All(online.Connections, connection => Assert.True(connection.IsOpen));
		Assert.Equal(2, online.Connections.Count());
		Assert.Same(online, await app.Users.ConnectBotAsync(Token));
		Assert.Equal(2, online.Connections.Count());
	}

	[Fact]
	public async Task OnlinePrivilegeGrant_Resets()
	{
		using var app = new TestApplication();
		var bot = await app.Users.EnsureBotAsync(Token);
		var online = await app.Users.ConnectBotAsync(Token);
		Assert.NotNull(online);
		var connections = online.Connections.ToList();
		bot.Value.Privileges = UserPrivileges.Tournament;

		var restored = await app.Users.EnsureBotAsync(Token);

		Assert.All(connections, connection => Assert.False(connection.IsOpen));
		Assert.Equal(UserPrivileges.None, restored.Value.Privileges);
	}

	private static async Task<UserRecord> StoredBotAsync(TestApplication app)
	{
		return await app.Provider.GetRequiredService<IUserRepository>().GetAsync(BasilBot.Id, Token)
		       ?? throw new InvalidOperationException("The bot is not stored.");
	}
}