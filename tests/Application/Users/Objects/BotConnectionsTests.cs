using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Authentication;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Users.Tests.Objects;

/// <summary>BasilBot is online through one osu! game client connection and one IRC connection that no client holds.</summary>
public sealed class BotConnectionsTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static async Task<(TestApplication App, User Bot)> StartAsync()
	{
		var app = new TestApplication();
		await app.SeedBotAsync();
		var bot = await app.Users.ConnectBotAsync(Token);
		Assert.NotNull(bot);
		return (app, bot);
	}

	[Fact]
	public async Task ConnectBotAsync_OpensExactlyOneStableAndOneIrcConnection()
	{
		var (app, bot) = await StartAsync();
		using var _ = app;

		var kinds = bot.Connections.Select(connection => connection.Type).Order().ToList();

		Assert.Equal([SessionType.Stable, SessionType.Irc], kinds);
		Assert.All(bot.Connections, connection => Assert.True(connection.IsOpen));
		Assert.All(bot.Connections, connection => Assert.True(connection.IsServerOwned));
	}

	[Fact]
	public async Task ConnectBotAsync_BringsTheBotOnlineOnce()
	{
		var (app, bot) = await StartAsync();
		using var _ = app;

		Assert.Single(app.Users.Online.Keys, user => user.IsBasilBot());
		Assert.Same(bot, app.Users.Online[bot.Record]);
	}

	[Fact]
	public async Task ConnectBotAsync_CalledAgain_OpensNothingNew()
	{
		var (app, bot) = await StartAsync();
		using var _ = app;
		var before = bot.Connections.ToList();

		var again = await app.Users.ConnectBotAsync(Token);

		Assert.Same(bot, again);
		Assert.Equal(before, again.Connections.ToList());
	}

	[Fact]
	public async Task ConnectBotAsync_OneConnectionWasClosed_OpensOnlyTheMissingOne()
	{
		var (app, bot) = await StartAsync();
		using var _ = app;
		var stable = bot.Connections.OfType<StableConnection>().Single();
		var irc = bot.Connections.OfType<IrcConnection>().Single();
		app.Users.Close(irc, SessionEndReason.ConnectionLost);

		await app.Users.ConnectBotAsync(Token);

		Assert.Same(stable, bot.Connections.OfType<StableConnection>().Single());
		var reopened = bot.Connections.OfType<IrcConnection>().Single();
		Assert.NotSame(irc, reopened);
		Assert.True(reopened.IsOpen);
	}

	[Fact]
	public async Task ConnectBotAsync_RecordIsNotStored_ReturnsNull()
	{
		using var app = new TestApplication();

		Assert.Null(await app.Users.ConnectBotAsync(Token));
	}

	[Fact]
	public async Task BotConnections_TokensCannotBeFound()
	{
		var (app, bot) = await StartAsync();
		using var _ = app;

		Assert.All(bot.Connections, connection => Assert.Null(app.Users.Find(connection.Token)));
	}

	[Fact]
	public async Task Bot_IsNotAnOsuGameClient()
	{
		var (app, bot) = await StartAsync();
		using var _ = app;

		Assert.Null(bot.Stable);
		Assert.Null(app.Users.OpenStableClient(bot.Record));
		Assert.Empty(app.Users.OpenStableClients());
	}

	[Fact]
	public async Task Bot_StaysOnlineAfterAnHourWithoutActivity()
	{
		var (app, bot) = await StartAsync();
		using var _ = app;
		app.Clock.Advance(TimeSpan.FromHours(1));

		Assert.Equal(0, app.Users.CloseIdle());
		Assert.Equal(2, bot.Connections.Count(connection => connection.IsOpen));
	}

	[Fact]
	public async Task CloseIdle_BotAndAnIdleClient_ClosesOnlyTheClient()
	{
		var (app, bot) = await StartAsync();
		using var _ = app;
		var user = await app.CreateUserAsync("IdleNeighbour", "password");
		var idle = await app.LoginAsync(user, SessionType.Stable);
		app.Clock.Advance(TimeSpan.FromHours(1));

		Assert.Equal(1, app.Users.CloseIdle());
		Assert.False(idle.IsOpen);
		Assert.All(bot.Connections, connection => Assert.True(connection.IsOpen));
	}
}
