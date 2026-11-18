using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Objects.Events;
using Basil.Domain.Authentication;
using Basil.Domain.Users;
using Basil.Infrastructure.Users.Workers;
using Basil.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Basil.Infrastructure.Users.Tests.Workers;

public sealed class IdleConnectionSweeperTests
{
	private static readonly TimeSpan SweepPeriod = TimeSpan.FromSeconds(100);

	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

	[Theory]
	[InlineData(SessionType.Stable)]
	[InlineData(SessionType.Irc)]
	[InlineData(SessionType.StableTourney)]
	public async Task ExecuteAsync_ClosesAnIdleGameIrcOrTourneyConnection(SessionType type)
	{
		using var app = new TestApplication();
		var privileges = type is SessionType.StableTourney
			? UserPrivilegesExtensions.Default | UserPrivileges.Tournament
			: UserPrivilegesExtensions.Default;
		var user = await app.CreateUserAsync("Idle", "password", privileges);
		var connection = await app.LoginAsync(user, type);
		app.Clock.Advance(TimeSpan.FromSeconds(301));
		var closed = NextCloseAsync(app, connection);
		using var running = await StartAsync(app);

		await SweepUntilAsync(app, closed);

		var @event = await closed;
		Assert.Equal(SessionEndReason.TimedOut, @event.Reason);
		Assert.False(connection.IsOpen);
	}

	[Fact]
	public async Task ExecuteAsync_LeavesAConnectionThatJustSpokeOpen()
	{
		using var app = new TestApplication();
		var quietUser = await app.CreateUserAsync("Quiet", "password");
		var quiet = await app.LoginAsync(quietUser, SessionType.Stable);
		var activeUser = await app.CreateUserAsync("Active", "password");
		var active = await app.LoginAsync(activeUser, SessionType.Stable);
		app.Clock.Advance(TimeSpan.FromSeconds(301));
		app.Users.MarkActive(active);
		var quietClosed = NextCloseAsync(app, quiet);
		using var running = await StartAsync(app);

		await SweepUntilAsync(app, quietClosed, active);

		Assert.True(active.IsOpen);
	}

	[Fact]
	public async Task ExecuteAsync_LeavesARecentlyActiveApiConnectionOpen()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Api", "password");
		var connection = await app.LoginAsync(user);
		var canaryUser = await app.CreateUserAsync("Canary", "password");
		var canary = await app.LoginAsync(canaryUser, SessionType.Stable);
		app.Clock.Advance(TimeSpan.FromSeconds(301));
		app.Users.MarkActive(connection);
		var canaryClosed = NextCloseAsync(app, canary);
		using var running = await StartAsync(app);

		await SweepUntilAsync(app, canaryClosed);

		Assert.True(connection.IsOpen);
	}

	[Fact]
	public async Task ExecuteAsync_ClosesAnApiConnectionQuietForMoreThan300Seconds()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Api", "password");
		var connection = await app.LoginAsync(user);
		app.Clock.Advance(TimeSpan.FromSeconds(301));
		var closed = NextCloseAsync(app, connection);
		using var running = await StartAsync(app);

		await SweepUntilAsync(app, closed);

		Assert.False(connection.IsOpen);
	}

	[Fact]
	public async Task ExecuteAsync_LeavesTheBotConnectionsOpen()
	{
		using var app = new TestApplication();
		await app.SeedBotAsync();
		var bot = await app.Users.ConnectBotAsync(TestContext.Current.CancellationToken);
		Assert.NotNull(bot);
		var canaryUser = await app.CreateUserAsync("Canary", "password");
		var canary = await app.LoginAsync(canaryUser, SessionType.Stable);
		app.Clock.Advance(TimeSpan.FromHours(3));
		var canaryClosed = NextCloseAsync(app, canary);
		using var running = await StartAsync(app);

		await SweepUntilAsync(app, canaryClosed);

		Assert.All(bot.Connections, connection => Assert.True(connection.IsOpen));
	}

	[Fact]
	public async Task ExecuteAsync_ClosesAConnectionOnlyOnce()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Once", "password");
		var first = await app.LoginAsync(user, SessionType.Stable);
		var firstClosed = NextCloseAsync(app, first);
		using var running = await StartAsync(app);
		await SweepUntilAsync(app, firstClosed);

		var laterUser = await app.CreateUserAsync("Later", "password");
		var later = await app.LoginAsync(laterUser, SessionType.Stable);
		var laterClosed = NextCloseAsync(app, later);
		await SweepUntilAsync(app, laterClosed);

		Assert.Single(app.UserEvents.Events.OfType<UserConnectionClosed>(),
			@event => ReferenceEquals(@event.Connection, first));
	}

	private static async Task<RunningSweeper> StartAsync(TestApplication app)
	{
		var time = new SignalingTimeProvider(app.Clock);
		var sweeper = new IdleConnectionSweeper(app.Users, time, NullLogger<IdleConnectionSweeper>.Instance);
		await sweeper.StartAsync(TestContext.Current.CancellationToken);
		await time.TimerCreated.WaitAsync(Patience, TestContext.Current.CancellationToken);
		return new RunningSweeper(sweeper);
	}

	private static async Task SweepUntilAsync(TestApplication app, Task until, params Connection[] keepActive)
	{
		for (var sweep = 0; sweep < 20 && !until.IsCompleted; sweep++)
		{
			app.Clock.Advance(SweepPeriod);
			foreach (var connection in keepActive) app.Users.MarkActive(connection);
			await Task.Yield();
		}

		await until.WaitAsync(Patience, TestContext.Current.CancellationToken);
	}

	private static Task<UserConnectionClosed> NextCloseAsync(TestApplication app, Connection connection)
	{
		var closed = new TaskCompletionSource<UserConnectionClosed>(TaskCreationOptions.RunContinuationsAsynchronously);
		app.UserEvents.Recorded += @event =>
		{
			if (@event is UserConnectionClosed close && ReferenceEquals(close.Connection, connection))
				closed.TrySetResult(close);
		};
		return closed.Task;
	}

	private sealed class RunningSweeper(IdleConnectionSweeper sweeper) : IDisposable
	{
		public void Dispose()
		{
			sweeper.Dispose();
		}
	}
}