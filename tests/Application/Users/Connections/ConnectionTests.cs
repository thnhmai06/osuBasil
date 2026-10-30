using System.Security.Cryptography;
using System.Text;
using Basil.Application.Users.Connections;
using Basil.Application.Users.Users;
using Basil.Domain.Auth;
using Basil.Testing;

namespace Basil.Application.Users.Tests.Connections;

public sealed class ConnectionTests
{
	[Fact]
	public async Task Close_MultipleConnections_GoesOfflineOnlyAfterLast()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("TwoConnections", "password");
		var first = await app.LoginAsync(user);
		var second = await app.LoginAsync(user);

		app.Users.Close(first, SessionEndReason.LoggedOut);
		var firstClosed = Assert.IsType<UserConnectionClosed>(app.UserEvents.Events[^1]);
		app.Users.Close(second, SessionEndReason.LoggedOut);
		var secondClosed = Assert.IsType<UserConnectionClosed>(app.UserEvents.Events[^1]);

		Assert.False(firstClosed.WentOffline);
		Assert.True(secondClosed.WentOffline);
		Assert.False(app.Users.Online.ContainsKey(user));
	}

	[Fact]
	public async Task Close_LoggedOutWithinOneSecondOfBanchoLogin_LeavesConnectionOpen()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("EarlyLogout", "password");
		var connection = await app.LoginAsync(user, SessionType.Osu);

		app.Users.Close(connection, SessionEndReason.LoggedOut);

		Assert.True(connection.IsOpen);
		Assert.DoesNotContain(app.UserEvents.Events, @event => @event is UserConnectionClosed);
	}

	[Theory]
	[InlineData(SessionType.Osu, 300)]
	[InlineData(SessionType.Api, 300)]
	public async Task CloseIdle_AtTimeoutBoundary_LeavesConnectionOpen(SessionType type, int timeoutSeconds)
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
	[InlineData(SessionType.Osu, 300)]
	[InlineData(SessionType.Api, 300)]
	public async Task CloseIdle_OneSecondPastTimeout_ClosesConnection(SessionType type, int timeoutSeconds)
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync($"Expired{type}", "password");
		var connection = await app.LoginAsync(user, type);
		app.Clock.Advance(TimeSpan.FromSeconds(timeoutSeconds + 1));

		var closedCount = app.Users.CloseIdle();

		Assert.Equal(1, closedCount);
		Assert.False(connection.IsOpen);
		Assert.Equal(SessionEndReason.TimedOut,
			Assert.IsType<UserConnectionClosed>(app.UserEvents.Events[^1]).Reason);
	}

	[Fact]
	public async Task CloseIdle_AfterMarkActive_UsesActivityTime()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("ActiveAgain", "password");
		var connection = await app.LoginAsync(user);
		app.Clock.Advance(TimeSpan.FromMinutes(4));
		app.Users.MarkActive(connection);
		app.Clock.Advance(TimeSpan.FromMinutes(4));

		Assert.Equal(0, app.Users.CloseIdle());
		Assert.True(connection.IsOpen);

		app.Clock.Advance(TimeSpan.FromMinutes(1).Add(TimeSpan.FromSeconds(1)));

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

	[Fact]
	public async Task Login_ApiSessionStoresOnlyTokenHashAndSlidesExpiry()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("ApiSession", "password");
		var connection = await app.LoginAsync(user, SessionType.Api);
		var session = connection.Session;

		Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(connection.Token)), session.TokenHash);
		Assert.Equal(app.Clock.GetUtcNow().AddDays(7), session.ExpiresAt);

		app.Clock.Advance(TimeSpan.FromDays(1));
		app.Users.MarkActive(connection);

		Assert.Equal(app.Clock.GetUtcNow().AddDays(7), session.ExpiresAt);
	}
}
