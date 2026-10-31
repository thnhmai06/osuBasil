using Basil.Application.Users.Handlers;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Objects.Events;
using Basil.Domain.Auth;
using Basil.Domain.Client;
using Basil.Domain.Users;
using Basil.Testing;
using Microsoft.Extensions.Logging.Abstractions;

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
		return new SpectatorBundle(SpectatorAction.Standard, [new SpectatorFrame(1, 0, 10, 20, 100)],
			new SpectatorScore(100, 0, 1, 0, 0, 0, 0, 0, 300, 1, 1, true, 200), 1);
	}

	private static async Task<(BanchoConnection Host, ApiConnection Watcher)> SetUpAsync(TestApplication app,
		UserPermissions? watcherPermissions = null)
	{
		var host = await RoomFixture.PlayerAsync(app, "Watched");
		var user = await app.CreateUserAsync("ApiWatcher", "password", watcherPermissions);
		var watcher = (ApiConnection)await app.LoginAsync(user);
		return (host, watcher);
	}

	[Fact]
	public async Task SpectateAsync_FromAnApiConnection_AddsItAsASpectator()
	{
		using var app = new TestApplication();
		var (host, watcher) = await SetUpAsync(app);

		var result = await host.ScopeTo(watcher).SpectateAsync();

		Assert.Equal(ConnectionOutcome.Ok, result.Outcome);
		Assert.Contains(watcher, host.Spectators);
		Assert.True(host.HasApiSpectators);
	}

	[Fact]
	public async Task SpectateAsync_WithoutTheWatchPermission_NamesIt()
	{
		using var app = new TestApplication();
		var (host, watcher) = await SetUpAsync(app, UserPermissionsGroups.Default & ~UserPermissions.SpectatingWatch);

		var result = await host.ScopeTo(watcher).SpectateAsync();

		Assert.False(result.IsOk);
		Assert.Equal(UserPermissions.SpectatingWatch, result.Missing);
		Assert.False(host.HasApiSpectators);
	}

	[Fact]
	public async Task SpectateAsync_WhenTheHostBlockedTheWatcher_IsRefused()
	{
		using var app = new TestApplication();
		var (host, watcher) = await SetUpAsync(app);
		Assert.True((await app.Users.Of(watcher.Record).ScopeTo(host).Relationships.BlockAsync(Token)).IsOk);

		var result = await host.ScopeTo(watcher).SpectateAsync();

		Assert.Equal(ConnectionOutcome.Blocked, result.Outcome);
		Assert.False(host.HasApiSpectators);
	}

	[Fact]
	public async Task PublishFrames_WithoutAnApiSpectator_PublishesNothing()
	{
		using var app = new TestApplication();
		var host = await RoomFixture.PlayerAsync(app, "Watched");
		var other = await RoomFixture.PlayerAsync(app, "OtherWatcher");
		Assert.True((await host.ScopeTo(other).SpectateAsync()).IsOk);

		Assert.False(host.ScopeTo(host).PublishFrames(Bundle()));
		Assert.Empty(app.UserEvents.Events.OfType<UserSpectatorFramesReceived>());
	}

	[Fact]
	public async Task PublishFrames_FromTheHost_ReachesTheApiSpectators()
	{
		using var app = new TestApplication();
		var (host, watcher) = await SetUpAsync(app);
		Assert.True((await host.ScopeTo(watcher).SpectateAsync()).IsOk);
		var bundle = Bundle();

		Assert.True(host.ScopeTo(host).PublishFrames(bundle));

		var published = Assert.Single(app.UserEvents.Events.OfType<UserSpectatorFramesReceived>());
		Assert.Same(host, published.Host);
		Assert.Same(bundle, published.Bundle);
	}

	[Fact]
	public async Task PublishFrames_FromAnotherConnection_IsRefused()
	{
		using var app = new TestApplication();
		var (host, watcher) = await SetUpAsync(app);
		Assert.True((await host.ScopeTo(watcher).SpectateAsync()).IsOk);

		Assert.False(host.ScopeTo(watcher).PublishFrames(Bundle()));
	}

	[Fact]
	public async Task ClosingTheApiConnection_StopsTheSpectating()
	{
		using var app = new TestApplication();
		var (host, watcher) = await SetUpAsync(app);
		Assert.True((await host.ScopeTo(watcher).SpectateAsync()).IsOk);

		app.Users.Close(watcher, SessionEndReason.ConnectionLost);
		var closed = Assert.Single(app.UserEvents.Events.OfType<UserConnectionClosed>(),
			@event => @event.Connection == watcher);
		await new ConnectionHandler(app.Users, NullLogger<ConnectionHandler>.Instance).Handle(closed, Token);

		Assert.DoesNotContain(watcher, host.Spectators);
		Assert.False(host.HasApiSpectators);
	}
}
