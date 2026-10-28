using Basil.Application.Chat.Runtime;
using Basil.Application.Users.Connections;
using Basil.Application.Users.Users;
using Basil.Domain.Auth;
using Basil.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Chat.Tests.Runtime;

/// <summary>
///     What the spectator channel handlers keep in step: the first spectator brings the spectated client in, the last
///     one takes it out, and a closed client's channel closes.
/// </summary>
public sealed class SpectatorChannelHandlerTests
{
	[Fact]
	public async Task Handle_FirstSpectator_JoinsTheSpectatorAndTheHost()
	{
		using var app = new TestApplication();
		var host = await RoomFixture.PlayerAsync(app, "Watched");
		var spectator = await RoomFixture.PlayerAsync(app, "Watcher");
		Assert.True((await host.ScopeTo(spectator).SpectateAsync()).IsOk);
		var channels = Channels(app);
		var joined = Assert.Single(app.UserEvents.Events.OfType<UserSpectatorJoined>());

		await new SpectatorJoinedHandler(channels).Handle(joined, TestContext.Current.CancellationToken);

		var channel = channels.Of(host);
		Assert.Contains(spectator, channel.Members);
		Assert.Contains(host, channel.Members);
	}

	[Fact]
	public async Task Handle_SecondSpectator_DoesNotJoinTheHostTwice()
	{
		using var app = new TestApplication();
		var host = await RoomFixture.PlayerAsync(app, "Watched");
		var first = await RoomFixture.PlayerAsync(app, "FirstWatcher");
		var second = await RoomFixture.PlayerAsync(app, "SecondWatcher");
		Assert.True((await host.ScopeTo(first).SpectateAsync()).IsOk);
		Assert.True((await host.ScopeTo(second).SpectateAsync()).IsOk);
		var channels = Channels(app);
		var handler = new SpectatorJoinedHandler(channels);
		var joined = app.UserEvents.Events.OfType<UserSpectatorJoined>().ToList();

		foreach (var @event in joined)
			await handler.Handle(@event, TestContext.Current.CancellationToken);

		var channel = channels.Of(host);
		Assert.Contains(first, channel.Members);
		Assert.Contains(second, channel.Members);
		Assert.Single(app.ChannelEvents.Events.OfType<ChannelMemberJoined>(),
			@event => @event.Channel == channel && @event.Member == host);
	}

	[Fact]
	public async Task Handle_ApiSpectator_JoinsNoChannel()
	{
		using var app = new TestApplication();
		var host = await RoomFixture.PlayerAsync(app, "Watched");
		var user = await app.CreateUserAsync("ApiWatcher", "password");
		var watcher = await app.LoginAsync(user);
		Assert.True((await host.ScopeTo(watcher).SpectateAsync()).IsOk);
		var channels = Channels(app);
		var joined = Assert.Single(app.UserEvents.Events.OfType<UserSpectatorJoined>());

		await new SpectatorJoinedHandler(channels).Handle(joined, TestContext.Current.CancellationToken);

		var channel = channels.Of(host);
		Assert.DoesNotContain(watcher, channel.Members);
		Assert.DoesNotContain(host, channel.Members);
	}

	[Fact]
	public async Task Handle_LastGameSpectatorLeaving_PartsTheHostEvenWhileAnApiConnectionWatches()
	{
		using var app = new TestApplication();
		var host = await RoomFixture.PlayerAsync(app, "Watched");
		var spectator = await RoomFixture.PlayerAsync(app, "Watcher");
		var apiWatcher = await app.LoginAsync(await app.CreateUserAsync("ApiWatcher", "password"));
		Assert.True((await host.ScopeTo(spectator).SpectateAsync()).IsOk);
		Assert.True((await host.ScopeTo(apiWatcher).SpectateAsync()).IsOk);
		var channels = Channels(app);
		await JoinBothAsync(app, channels, host);
		Assert.True(host.ScopeTo(spectator).StopSpectating());
		var left = Assert.Single(app.UserEvents.Events.OfType<UserSpectatorLeft>());

		await new SpectatorLeftHandler(channels).Handle(left, TestContext.Current.CancellationToken);

		Assert.DoesNotContain(host, channels.Of(host).Members);
	}

	[Fact]
	public async Task Handle_RepeatedJoin_LeavesTheChannelAsItWas()
	{
		using var app = new TestApplication();
		var host = await RoomFixture.PlayerAsync(app, "Watched");
		var spectator = await RoomFixture.PlayerAsync(app, "Watcher");
		Assert.True((await host.ScopeTo(spectator).SpectateAsync()).IsOk);
		Assert.True((await host.ScopeTo(spectator).SpectateAsync()).IsOk);
		var channels = Channels(app);
		var handler = new SpectatorJoinedHandler(channels);
		var joined = app.UserEvents.Events.OfType<UserSpectatorJoined>().ToList();

		foreach (var @event in joined)
			await handler.Handle(@event, TestContext.Current.CancellationToken);

		var channel = channels.Of(host);
		Assert.Single(app.ChannelEvents.Events.OfType<ChannelMemberJoined>(),
			@event => @event.Channel == channel && @event.Member == spectator);
		Assert.Single(app.ChannelEvents.Events.OfType<ChannelMemberJoined>(),
			@event => @event.Channel == channel && @event.Member == host);
	}

	[Fact]
	public async Task Handle_LeftSpectator_PartsTheSpectatorAndKeepsTheHost()
	{
		using var app = new TestApplication();
		var host = await RoomFixture.PlayerAsync(app, "Watched");
		var first = await RoomFixture.PlayerAsync(app, "FirstWatcher");
		var second = await RoomFixture.PlayerAsync(app, "SecondWatcher");
		Assert.True((await host.ScopeTo(first).SpectateAsync()).IsOk);
		Assert.True((await host.ScopeTo(second).SpectateAsync()).IsOk);
		var channels = Channels(app);
		await JoinBothAsync(app, channels, host);
		Assert.True(host.ScopeTo(second).StopSpectating());
		var left = Assert.Single(app.UserEvents.Events.OfType<UserSpectatorLeft>());

		await new SpectatorLeftHandler(channels).Handle(left, TestContext.Current.CancellationToken);

		var channel = channels.Of(host);
		Assert.DoesNotContain(second, channel.Members);
		Assert.Contains(first, channel.Members);
		Assert.Contains(host, channel.Members);
	}

	[Fact]
	public async Task Handle_LastSpectatorLeaving_PartsTheHostToo()
	{
		using var app = new TestApplication();
		var host = await RoomFixture.PlayerAsync(app, "Watched");
		var spectator = await RoomFixture.PlayerAsync(app, "Watcher");
		Assert.True((await host.ScopeTo(spectator).SpectateAsync()).IsOk);
		var channels = Channels(app);
		await JoinBothAsync(app, channels, host);
		Assert.True(host.ScopeTo(spectator).StopSpectating());
		var left = Assert.Single(app.UserEvents.Events.OfType<UserSpectatorLeft>());

		await new SpectatorLeftHandler(channels).Handle(left, TestContext.Current.CancellationToken);

		var channel = channels.Of(host);
		Assert.DoesNotContain(spectator, channel.Members);
		Assert.DoesNotContain(host, channel.Members);
	}

	[Fact]
	public async Task Handle_ConnectionClosed_ClosesTheSpectatorChannel()
	{
		using var app = new TestApplication();
		var host = await RoomFixture.PlayerAsync(app, "Watched");
		var spectator = await RoomFixture.PlayerAsync(app, "Watcher");
		Assert.True((await host.ScopeTo(spectator).SpectateAsync()).IsOk);
		var channels = Channels(app);
		await JoinBothAsync(app, channels, host);
		var channel = channels.Of(host);
		app.Users.Close(host, SessionEndReason.ConnectionLost);
		var closed = Assert.Single(app.UserEvents.Events.OfType<UserConnectionClosed>(),
			@event => @event.Connection == host);

		await new ConnectionSpectatorChannelHandler(channels).Handle(closed,
			TestContext.Current.CancellationToken);

		Assert.True(channel.IsClosed);
		Assert.Empty(channel.Members);
		Assert.Null(channels.Remove(host));
	}

	private static SpectatorChannels Channels(TestApplication app)
	{
		return app.Provider.GetRequiredService<SpectatorChannels>();
	}

	/// <summary>Runs the joined handler for every spectator of a client, as the event queue would.</summary>
	private static async Task JoinBothAsync(TestApplication app, SpectatorChannels channels, BanchoConnection host)
	{
		var handler = new SpectatorJoinedHandler(channels);
		foreach (var @event in app.UserEvents.Events.OfType<UserSpectatorJoined>()
			         .Where(@event => @event.Host == host))
			await handler.Handle(@event, TestContext.Current.CancellationToken);
	}
}