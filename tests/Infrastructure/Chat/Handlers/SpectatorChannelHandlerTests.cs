using Basil.Application.Chat.Objects.Events;
using Basil.Application.Chat.Registry;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Objects.Events;
using Basil.Domain.Authentication;
using Basil.Infrastructure.Chat.Handlers;
using Basil.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Chat.Tests.Handlers;

/// <summary>
///     What the spectator channel handlers keep in step: the first spectator brings the spectated client in, the last
///     one takes it out, and a closed client's channel closes.
/// </summary>
public sealed class SpectatorChannelHandlerTests
{
	[Fact]
	public async Task FirstSpectator_JoinsHost()
	{
		using var app = new TestApplication();
		var host = await RoomFixture.PlayerAsync(app, "Watched");
		var spectator = await RoomFixture.PlayerAsync(app, "Watcher");
		Assert.True((await host.ScopeTo(spectator).SpectateAsync()) == ConnectionOutcome.Ok);
		var channels = Channels(app);
		var joined = Assert.Single(app.UserEvents.Events.OfType<UserSpectatorJoined>());

		await new SpectatorJoinedHandler(channels).Handle(joined, TestContext.Current.CancellationToken);

		var channel = channels.Of(host);
		Assert.Contains(spectator, channel.Members);
		Assert.Contains(host, channel.Members);
	}

	[Fact]
	public async Task SecondSpectator_HostNotTwice()
	{
		using var app = new TestApplication();
		var host = await RoomFixture.PlayerAsync(app, "Watched");
		var first = await RoomFixture.PlayerAsync(app, "FirstWatcher");
		var second = await RoomFixture.PlayerAsync(app, "SecondWatcher");
		Assert.True((await host.ScopeTo(first).SpectateAsync()) == ConnectionOutcome.Ok);
		Assert.True((await host.ScopeTo(second).SpectateAsync()) == ConnectionOutcome.Ok);
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
	public async Task ApiSpectator_NoChannel()
	{
		using var app = new TestApplication();
		var host = await RoomFixture.PlayerAsync(app, "Watched");
		var user = await app.CreateUserAsync("ApiWatcher", "password");
		var watcher = await app.LoginAsync(user);
		Assert.True((await host.ScopeTo(watcher).SpectateAsync()) == ConnectionOutcome.Ok);
		var channels = Channels(app);
		var joined = Assert.Single(app.UserEvents.Events.OfType<UserSpectatorJoined>());

		await new SpectatorJoinedHandler(channels).Handle(joined, TestContext.Current.CancellationToken);

		var channel = channels.Of(host);
		Assert.DoesNotContain(watcher, channel.Members);
		Assert.DoesNotContain(host, channel.Members);
	}

	[Fact]
	public async Task LastGameSpectator_PartsHost()
	{
		using var app = new TestApplication();
		var host = await RoomFixture.PlayerAsync(app, "Watched");
		var spectator = await RoomFixture.PlayerAsync(app, "Watcher");
		var apiWatcher = await app.LoginAsync(await app.CreateUserAsync("ApiWatcher", "password"));
		Assert.True((await host.ScopeTo(spectator).SpectateAsync()) == ConnectionOutcome.Ok);
		Assert.True((await host.ScopeTo(apiWatcher).SpectateAsync()) == ConnectionOutcome.Ok);
		var channels = Channels(app);
		await JoinBothAsync(app, channels, host);
		Assert.True(host.ScopeTo(spectator).StopSpectating());
		var left = Assert.Single(app.UserEvents.Events.OfType<UserSpectatorLeft>());

		await new SpectatorLeftHandler(channels).Handle(left, TestContext.Current.CancellationToken);

		Assert.DoesNotContain(host, channels.Of(host).Members);
	}

	[Fact]
	public async Task RepeatedJoin_Unchanged()
	{
		using var app = new TestApplication();
		var host = await RoomFixture.PlayerAsync(app, "Watched");
		var spectator = await RoomFixture.PlayerAsync(app, "Watcher");
		Assert.True((await host.ScopeTo(spectator).SpectateAsync()) == ConnectionOutcome.Ok);
		Assert.True((await host.ScopeTo(spectator).SpectateAsync()) == ConnectionOutcome.Ok);
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
	public async Task LeftSpectator_HostKept()
	{
		using var app = new TestApplication();
		var host = await RoomFixture.PlayerAsync(app, "Watched");
		var first = await RoomFixture.PlayerAsync(app, "FirstWatcher");
		var second = await RoomFixture.PlayerAsync(app, "SecondWatcher");
		Assert.True((await host.ScopeTo(first).SpectateAsync()) == ConnectionOutcome.Ok);
		Assert.True((await host.ScopeTo(second).SpectateAsync()) == ConnectionOutcome.Ok);
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
	public async Task LastSpectator_PartsHost()
	{
		using var app = new TestApplication();
		var host = await RoomFixture.PlayerAsync(app, "Watched");
		var spectator = await RoomFixture.PlayerAsync(app, "Watcher");
		Assert.True((await host.ScopeTo(spectator).SpectateAsync()) == ConnectionOutcome.Ok);
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
	public async Task ConnectionClosed_ChannelClosed()
	{
		using var app = new TestApplication();
		var host = await RoomFixture.PlayerAsync(app, "Watched");
		var spectator = await RoomFixture.PlayerAsync(app, "Watcher");
		Assert.True((await host.ScopeTo(spectator).SpectateAsync()) == ConnectionOutcome.Ok);
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
	private static async Task JoinBothAsync(TestApplication app, SpectatorChannels channels, StableConnection host)
	{
		var handler = new SpectatorJoinedHandler(channels);
		foreach (var @event in app.UserEvents.Events.OfType<UserSpectatorJoined>()
			         .Where(@event => @event.Host == host))
			await handler.Handle(@event, TestContext.Current.CancellationToken);
	}
}