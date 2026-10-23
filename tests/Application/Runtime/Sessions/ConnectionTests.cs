using Basil.Application.Runtime.Sessions;
using Basil.Testing;

namespace Basil.Application.Runtime.Tests.Sessions;

public sealed class ConnectionTests
{
	[Fact]
	public async Task Close_MultipleConnections_GoesOfflineOnlyAfterLast()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("TwoConnections", "password");
		var first = await app.LoginAsync(user);
		var second = await app.LoginAsync(user);

		app.Users.Close(first, ConnectionCloseReason.LoggedOut);
		var firstClosed = Assert.IsType<UserConnectionClosed>(app.UserEvents.Events.Last());
		app.Users.Close(second, ConnectionCloseReason.LoggedOut);
		var secondClosed = Assert.IsType<UserConnectionClosed>(app.UserEvents.Events.Last());

		Assert.False(firstClosed.WentOffline);
		Assert.True(secondClosed.WentOffline);
		Assert.False(app.Users.Online.ContainsKey(user));
	}

	[Fact]
	public async Task Close_LoggedOutWithinOneSecondOfBanchoLogin_LeavesConnectionOpen()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("EarlyLogout", "password");
		var connection = await app.LoginAsync(user, ConnectionType.Bancho);

		app.Users.Close(connection, ConnectionCloseReason.LoggedOut);

		Assert.True(connection.IsOpen);
		Assert.DoesNotContain(app.UserEvents.Events, @event => @event is UserConnectionClosed);
	}

	[Theory]
	[InlineData(ConnectionType.Bancho, 300)]
	[InlineData(ConnectionType.Api, 7200)]
	public async Task CloseIdle_AtTimeoutBoundary_LeavesConnectionOpen(ConnectionType type, int timeoutSeconds)
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync($"Boundary{type}", "password");
		var connection = await app.LoginAsync(user, type);
		app.Clock.Advance(TimeSpan.FromSeconds(timeoutSeconds));

		var closedCount = app.Users.CloseIdle();

		Assert.Equal(0, closedCount);
		Assert.True(connection.IsOpen);
	}

	[Theory]
	[InlineData(ConnectionType.Bancho, 300)]
	[InlineData(ConnectionType.Api, 7200)]
	public async Task CloseIdle_OneSecondPastTimeout_ClosesConnection(ConnectionType type, int timeoutSeconds)
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync($"Expired{type}", "password");
		var connection = await app.LoginAsync(user, type);
		app.Clock.Advance(TimeSpan.FromSeconds(timeoutSeconds + 1));

		var closedCount = app.Users.CloseIdle();

		Assert.Equal(1, closedCount);
		Assert.False(connection.IsOpen);
		Assert.Equal(ConnectionCloseReason.TimedOut,
			Assert.IsType<UserConnectionClosed>(app.UserEvents.Events.Last()).Reason);
	}

	[Fact]
	public async Task CloseIdle_AfterMarkActive_UsesActivityTime()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("ActiveAgain", "password");
		var connection = await app.LoginAsync(user);
		app.Clock.Advance(TimeSpan.FromHours(1));
		app.Users.MarkActive(connection);
		app.Clock.Advance(TimeSpan.FromHours(1));

		Assert.Equal(0, app.Users.CloseIdle());
		Assert.True(connection.IsOpen);

		app.Clock.Advance(TimeSpan.FromHours(1).Add(TimeSpan.FromSeconds(1)));

		Assert.Equal(1, app.Users.CloseIdle());
		Assert.False(connection.IsOpen);
	}

	[Fact]
	public async Task CloseIdle_BotConnection_RemainsOpen()
	{
		using var app = new TestApplication();
		await app.SeedBotAsync();
		var bot = await app.Users.ConnectBotAsync(TestContext.Current.CancellationToken);
		app.Clock.Advance(TimeSpan.FromDays(1));

		Assert.NotNull(bot);
		Assert.Equal(0, app.Users.CloseIdle());
		Assert.True(bot.IsOpen);
	}

	[Fact]
	public async Task Find_OpenConnectionAndBotToken_ReturnsOnlyClientConnection()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("TokenUser", "password");
		var connection = await app.LoginAsync(user);
		await app.SeedBotAsync();
		var bot = await app.Users.ConnectBotAsync(TestContext.Current.CancellationToken);

		Assert.Same(connection, app.Users.Find(connection.Token));
		Assert.NotNull(bot);
		Assert.Null(app.Users.Find(bot.Token));
	}
}