using System.Drawing;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Client;
using Basil.Domain.Common;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Infrastructure.Users.Handlers;
using Basil.Testing;

namespace Basil.Infrastructure.Users.Tests.Handlers;

/// <summary>The messages that start, stop and report spectating, publish the watched play and set the presence status.</summary>
public sealed class SpectatingHandlerTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task SpectateUser_AddsSender()
	{
		using var app = new TestApplication();
		var (host, spectator) = await TwoAsync(app);

		var result = await new SpectateUserHandler().Handle(new SpectateUser(spectator, host), Token);

		Assert.True(result == ConnectionOutcome.Ok);
		Assert.Contains(spectator, host.Spectators);
	}

	[Fact]
	public async Task StopSpectating_RemovesSender()
	{
		using var app = new TestApplication();
		var (host, spectator) = await TwoAsync(app);
		await new SpectateUserHandler().Handle(new SpectateUser(spectator, host), Token);

		var stopped = await new StopSpectatingHandler().Handle(new StopSpectating(spectator, host), Token);

		Assert.True(stopped);
		Assert.DoesNotContain(spectator, host.Spectators);
	}

	[Fact]
	public async Task NotSpectatingStop_DoesNothing()
	{
		using var app = new TestApplication();
		var (host, stranger) = await TwoAsync(app);

		var stopped = await new StopSpectatingHandler().Handle(new StopSpectating(stranger, host), Token);

		Assert.False(stopped);
	}

	[Fact]
	public async Task SpectatingFailure_Accepted()
	{
		using var app = new TestApplication();
		var (host, spectator) = await TwoAsync(app);
		await new SpectateUserHandler().Handle(new SpectateUser(spectator, host), Token);

		var reported = await new ReportSpectatingFailedHandler()
			.Handle(new ReportSpectatingFailed(spectator, host), Token);

		Assert.True(reported);
	}

	[Fact]
	public async Task NotSpectatingFailure_Ignored()
	{
		using var app = new TestApplication();
		var (host, stranger) = await TwoAsync(app);

		var reported = await new ReportSpectatingFailedHandler()
			.Handle(new ReportSpectatingFailed(stranger, host), Token);

		Assert.False(reported);
	}

	[Fact]
	public async Task SetPlayerStatus_StoresOnClient()
	{
		using var app = new TestApplication();
		var player = await RoomFixture.PlayerAsync(app, "Player");
		var status = new PlayerStatus(UserActivity.Afk, "away", null, null, GameMods.NoMod, GameMode.Standard);

		var changed = await new SetPlayerStatusHandler().Handle(new SetPlayerStatus(player, status), Token);

		Assert.True(changed);
		Assert.Equal(status, player.Status);
	}

	[Fact]
	public async Task RepeatedPlayerStatus_ChangesNothing()
	{
		using var app = new TestApplication();
		var player = await RoomFixture.PlayerAsync(app, "Player");

		var changed = await new SetPlayerStatusHandler().Handle(new SetPlayerStatus(player, PlayerStatus.Idle), Token);

		Assert.False(changed);
	}

	[Fact]
	public async Task OwnIrcSession_ReturnsKey()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Irc", "password"));

		var (result, key, session) = await new OpenIrcSessionHandler(app.Users)
			.Handle(new OpenIrcSession(actor, UserReference.ById(actor.Record.Id), "laptop"), Token);

		Assert.True(result == UserOutcome.Ok);
		Assert.False(string.IsNullOrEmpty(key));
		Assert.NotNull(session);
	}

	[Fact]
	public async Task OtherIrcSession_NotSelf()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Plain", "password"));
		var other = await app.CreateUserAsync("Other", "password");

		var (result, key, session) = await new OpenIrcSessionHandler(app.Users)
			.Handle(new OpenIrcSession(actor, UserReference.ById(other.Id), "laptop"), Token);

		Assert.Equal(UserOutcome.NotSelf, result);
		Assert.Null(key);
		Assert.Null(session);
	}

	[Fact]
	public async Task ApiSpectatorFrames_Published()
	{
		using var app = new TestApplication();
		var host = await RoomFixture.PlayerAsync(app, "Watched");
		var watcher = await app.LoginAsync(await app.CreateUserAsync("ApiWatcher", "password"));
		Assert.True((await host.ScopeTo(watcher).SpectateAsync()) == ConnectionOutcome.Ok);

		var published = await new PublishSpectatorFramesHandler().Handle(new PublishSpectatorFrames(host, Bundle()), Token);

		Assert.True(published);
	}

	[Fact]
	public async Task NoApiSpectator_NothingPublished()
	{
		using var app = new TestApplication();
		var player = await RoomFixture.PlayerAsync(app, "Player");

		var published = await new PublishSpectatorFramesHandler().Handle(new PublishSpectatorFrames(player, Bundle()), Token);

		Assert.False(published);
	}

	private static SpectatorBundle Bundle()
	{
		return new SpectatorBundle(SpectatorAction.Standard,
			[new ReplayFrame(KeyState.Left1, TaikoDrumState.None, new PointF(10, 20), TimeSpan.FromMilliseconds(100))],
			new ScoreFrame(TimeSpan.FromMilliseconds(100), 0,
				new ScoreState(new HitCounts(1, 0, 0, 0, 0, 0), 300, 1, 1, true), 200, 0, null), 1);
	}

	private static async Task<(StableConnection Host, StableConnection Other)> TwoAsync(TestApplication app)
	{
		return (await RoomFixture.PlayerAsync(app, "Host"), await RoomFixture.PlayerAsync(app, "Other"));
	}
}
