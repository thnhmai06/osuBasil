using Basil.Application.Chat.Runtime;
using Basil.Application.Users.Connections;
using Basil.Application.Users.Users;
using Basil.Domain.Auth;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Chat.Tests.Runtime;

/// <summary>Keeps private-message-channel membership and lifetime in step with connection events.</summary>
public sealed class PmChannelHandlerTests
{
	[Fact]
	public async Task Handle_ConnectionOpened_AddsTheConnection()
	{
		using var app = new TestApplication();
		var record = await app.CreateUserAsync("Opened", "password");
		var connection = await app.LoginAsync(record, SessionType.Irc);
		var opened = Assert.Single(app.UserEvents.Events.OfType<UserConnectionOpened>());

		await new PmConnectionOpenedHandler(app.PmChannels).Handle(opened, TestContext.Current.CancellationToken);

		var channel = app.PmChannels.Of(app.Users.Of(record));
		Assert.Contains(connection, channel.Members);
	}

	[Fact]
	public async Task Handle_TourneyConnectionOpened_DoesNotOpenAChannel()
	{
		using var app = new TestApplication();
		var record = await app.CreateUserAsync("TourneyObserver", "password",
			UserPermissionsGroups.Default | UserPermissions.RoomObserve);
		await app.LoginAsync(record, SessionType.OsuTourney);
		var opened = Assert.Single(app.UserEvents.Events.OfType<UserConnectionOpened>());

		await new PmConnectionOpenedHandler(app.PmChannels).Handle(opened, TestContext.Current.CancellationToken);

		Assert.Null(app.PmChannels.Find(app.Users.Of(record)));
	}

	[Fact]
	public async Task Handle_ConnectionClosed_PartsItButKeepsTheChannelOpen()
	{
		using var app = new TestApplication();
		var record = await app.CreateUserAsync("StillHere", "password");
		var irc = await app.LoginAsync(record, SessionType.Irc);
		var bancho = await app.LoginAsync(record, SessionType.Osu);
		var owner = app.Users.Of(record);
		var channel = app.PmChannels.Of(owner);
		app.Users.Close(irc, SessionEndReason.ConnectionLost);
		var closed = Assert.Single(app.UserEvents.Events.OfType<UserConnectionClosed>(),
			@event => @event.Connection == irc);

		await new PmConnectionClosedHandler(app.PmChannels).Handle(closed, TestContext.Current.CancellationToken);

		Assert.DoesNotContain(irc, channel.Members);
		Assert.Contains(bancho, channel.Members);
		Assert.False(channel.IsClosed);
		Assert.Same(channel, app.PmChannels.Find(owner));
	}

	[Fact]
	public async Task Handle_LastConnectionClosed_ClosesAndForgetsTheChannel()
	{
		using var app = new TestApplication();
		var record = await app.CreateUserAsync("Leaving", "password");
		var connection = await app.LoginAsync(record, SessionType.Irc);
		var owner = app.Users.Of(record);
		var channel = app.PmChannels.Of(owner);
		app.Users.Close(connection, SessionEndReason.LoggedOut);
		var closed = Assert.Single(app.UserEvents.Events.OfType<UserConnectionClosed>(),
			@event => @event.Connection == connection);

		await new PmConnectionClosedHandler(app.PmChannels).Handle(closed, TestContext.Current.CancellationToken);

		Assert.True(channel.IsClosed);
		Assert.Empty(channel.Members);
		Assert.Null(app.PmChannels.Find(owner));
	}
}