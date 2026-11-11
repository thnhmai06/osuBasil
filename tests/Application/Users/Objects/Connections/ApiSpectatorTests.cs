using System.Drawing;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Objects.Events;
using Basil.Domain.Authentication;
using Basil.Domain.Client;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Infrastructure.Users.Handlers;
using Basil.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Basil.Application.Users.Objects;

namespace Basil.Application.Users.Tests.Objects.Connections;

/// <summary>
///     An HTTP API connection spectates an osu! client under the rules of the game client and only watches: the host
///     decodes frames only while an API connection watches.
/// </summary>
public sealed class ApiSpectatorTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static SpectatorBundle Bundle()
	{
		return new SpectatorBundle(SpectatorAction.Standard,
			[new ReplayFrame(KeyState.Left1, TaikoDrumState.None, new PointF(10, 20), TimeSpan.FromMilliseconds(100))],
			new ScoreFrame(TimeSpan.FromMilliseconds(100), 0,
				new ScoreState(new HitCounts(1, 0, 0, 0, 0, 0), 300, 1, 1, true), 200, 0, null), 1);
	}

	private static async Task<(StableConnection Host, ApiConnection Watcher)> SetUpAsync(TestApplication app,
		UserPrivileges? watcherPrivileges = null)
	{
		var host = await RoomFixture.PlayerAsync(app, "Watched");
		var user = await app.CreateUserAsync("ApiWatcher", "password", watcherPrivileges);
		var watcher = (ApiConnection)await app.LoginAsync(user);
		return (host, watcher);
	}

	[Fact]
	public async Task ApiConnectionSpectate_Added()
	{
		using var app = new TestApplication();
		var (host, watcher) = await SetUpAsync(app);

		var result = await host.ScopeTo(watcher).SpectateAsync();

		Assert.Equal(ConnectionOutcome.Ok, result);
		Assert.Contains(watcher, host.Spectators);
		Assert.True(host.HasApiSpectators);
	}

	[Fact]
	public async Task SpectateWithoutWatch_NamesPrivilege()
	{
		using var app = new TestApplication();
		var (host, watcher) = await SetUpAsync(app, UserPrivilegesExtensions.Default & ~UserPrivileges.Player);

		var result = await host.ScopeTo(watcher).SpectateAsync();

		Assert.False(result == ConnectionOutcome.Ok);
		Assert.Equal(ConnectionOutcome.Denied, result);
		Assert.False(host.HasApiSpectators);
	}

	[Fact]
	public async Task BlockedWatcher_Refused()
	{
		using var app = new TestApplication();
		var (host, watcher) = await SetUpAsync(app);
		Assert.True((await app.Users.Of(watcher.Record).ScopeTo(host).Relationships.BlockAsync()) == UserOutcome.Ok);

		var result = await host.ScopeTo(watcher).SpectateAsync();

		Assert.Equal(ConnectionOutcome.Blocked, result);
		Assert.False(host.HasApiSpectators);
	}

	[Fact]
	public async Task NoSpectator_PublishesNothing()
	{
		using var app = new TestApplication();
		var host = await RoomFixture.PlayerAsync(app, "Watched");
		var other = await RoomFixture.PlayerAsync(app, "OtherWatcher");
		Assert.True((await host.ScopeTo(other).SpectateAsync()) == ConnectionOutcome.Ok);

		Assert.False(host.ScopeTo(host).PublishFrames(Bundle()));
		Assert.Empty(app.UserEvents.Events.OfType<UserSpectatorFramesReceived>());
	}

	[Fact]
	public async Task HostFrames_ReachSpectators()
	{
		using var app = new TestApplication();
		var (host, watcher) = await SetUpAsync(app);
		Assert.True((await host.ScopeTo(watcher).SpectateAsync()) == ConnectionOutcome.Ok);
		var bundle = Bundle();

		Assert.True(host.ScopeTo(host).PublishFrames(bundle));

		var published = Assert.Single(app.UserEvents.Events.OfType<UserSpectatorFramesReceived>());
		Assert.Same(host, published.Host);
		Assert.Same(bundle, published.Bundle);
	}

	[Fact]
	public async Task ForeignFramePublish_Refused()
	{
		using var app = new TestApplication();
		var (host, watcher) = await SetUpAsync(app);
		Assert.True((await host.ScopeTo(watcher).SpectateAsync()) == ConnectionOutcome.Ok);

		Assert.False(host.ScopeTo(watcher).PublishFrames(Bundle()));
	}

	[Fact]
	public async Task ApiConnectionClosed_SpectatingStops()
	{
		using var app = new TestApplication();
		var (host, watcher) = await SetUpAsync(app);
		Assert.True((await host.ScopeTo(watcher).SpectateAsync()) == ConnectionOutcome.Ok);

		app.Users.Close(watcher, SessionEndReason.ConnectionLost);
		var closed = Assert.Single(app.UserEvents.Events.OfType<UserConnectionClosed>(),
			@event => @event.Connection == watcher);
		await new ConnectionHandler(app.Users, NullLogger<ConnectionHandler>.Instance).Handle(closed, Token);

		Assert.DoesNotContain(watcher, host.Spectators);
		Assert.False(host.HasApiSpectators);
	}
}