using System.Net;
using Basil.Application.Persistence.Users;
using Basil.Application.Runtime.Sessions;
using Basil.Domain.Common;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Runtime.Tests.Users;

public sealed class UserRegistryTests
{
	[Fact]
	public async Task LoginAsync_UnknownName_ReturnsUnknownUser()
	{
		using var app = new TestApplication();

		var result = await app.AttemptLoginAsync("MissingUser", "password");

		Assert.Equal(LoginFailure.UnknownUser, result.Failure);
	}

	[Fact]
	public async Task LoginAsync_WrongPassword_ReturnsWrongPassword()
	{
		using var app = new TestApplication();
		await app.CreateUserAsync("PasswordUser", "right-password");

		var result = await app.AttemptLoginAsync("PasswordUser", "wrong-password");

		Assert.Equal(LoginFailure.WrongPassword, result.Failure);
	}

	[Fact]
	public async Task LoginAsync_DeletedAccount_ReturnsAccountDeleted()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("DeletedUser", "password");
		user.Value.DeletedAt = app.Clock.GetUtcNow();

		var result = await app.AttemptLoginAsync("DeletedUser", "password");

		Assert.Equal(LoginFailure.AccountDeleted, result.Failure);
	}

	[Fact]
	public async Task LoginAsync_CorrectCredentials_PublishesCameOnline()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("OnlineUser", "password");

		var result = await app.AttemptLoginAsync("OnlineUser", "password");

		Assert.True(result.Succeeded);
		Assert.True(result.Connection!.IsOpen);
		Assert.Same(app.Users.Online[user], result.Connection.User);
		var opened = Assert.IsType<UserConnectionOpened>(app.UserEvents.Events.Last());
		Assert.Same(result.Connection, opened.Connection);
		Assert.True(opened.CameOnline);
	}

	[Fact]
	public async Task LoginAsync_BotConnectionRequested_ThrowsArgumentException()
	{
		using var app = new TestApplication();

		await Assert.ThrowsAsync<ArgumentException>(() => app.Users.LoginAsync(
			new LoginAttempt("unused", new Md5("0123456789abcdef0123456789abcdef")), ConnectionType.Bot,
			IPAddress.Loopback, null, 0, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task LoginAsync_BasilBotName_ReturnsUnknownUser()
	{
		using var app = new TestApplication();
		await app.SeedBotAsync();

		var result = await app.AttemptLoginAsync("BasilBot", "basil-bot");

		Assert.Equal(LoginFailure.UnknownUser, result.Failure);
	}

	[Fact]
	public async Task LoginAsync_SecondBanchoLoginWithinIdleWindow_ReturnsAlreadyOnline()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("AlreadyOnline", "password");
		await app.LoginAsync(user, ConnectionType.Bancho);
		app.Clock.Advance(TimeSpan.FromSeconds(9));

		var result = await app.AttemptLoginAsync(user.Value.Name, "password", ConnectionType.Bancho);

		Assert.Equal(LoginFailure.AlreadyOnline, result.Failure);
	}

	[Fact]
	public async Task LoginAsync_SecondBanchoLoginAfterIdleWindow_ReplacesConnection()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("ReplaceIdle", "password");
		var old = await app.LoginAsync(user, ConnectionType.Bancho);
		app.Clock.Advance(TimeSpan.FromSeconds(10));

		var result = await app.AttemptLoginAsync(user.Value.Name, "password", ConnectionType.Bancho);

		Assert.True(result.Succeeded);
		Assert.False(old.IsOpen);
		Assert.NotSame(old, result.Connection);
		var closed = Assert.IsType<UserConnectionClosed>(app.UserEvents.Events
			.Last(@event => @event is UserConnectionClosed));
		Assert.Equal(ConnectionCloseReason.Replaced, closed.Reason);
	}

	[Fact]
	public async Task LoginAsync_TourneyWithoutRoomObserve_ReturnsNoTourneyPermission()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("NoTourney", "password", UserPermissionsGroups.Default);

		var result = await app.AttemptLoginAsync(user.Value.Name, "password", ConnectionType.Tourney);

		Assert.Equal(LoginFailure.NoTourneyPermission, result.Failure);
	}

	[Theory]
	[InlineData(ConnectionType.Bancho)]
	[InlineData(ConnectionType.Tourney)]
	public async Task LoginAsync_OsuKindWithoutClient_ThrowsArgumentNullException(ConnectionType type)
	{
		using var app = new TestApplication();

		await Assert.ThrowsAsync<ArgumentNullException>(() => app.Users.LoginAsync(
			new LoginAttempt("unused", new Md5("0123456789abcdef0123456789abcdef")), type,
			IPAddress.Loopback, null, 0, TestContext.Current.CancellationToken));
	}
}