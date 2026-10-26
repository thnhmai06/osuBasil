using Basil.Application.Runtime.Sessions;
using Basil.Domain.Users;
using Basil.Testing;
using Basil.Infrastructure.Runtime.Sessions;
using Basil.Infrastructure.Runtime.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Basil.Infrastructure.Runtime.Tests.Sessions;

/// <summary>
///     Pins that a sweep closes what has been quiet for the time its kind allows, leaves a connection that just spoke
///     and the server's own bot alone, and closes each connection once.
/// </summary>
public sealed class IdleConnectionSweeperTests
{
	/// <summary>The interval between two sweeps of the sweeper.</summary>
	private static readonly TimeSpan SweepPeriod = TimeSpan.FromSeconds(100);

	/// <summary>How long a test waits for a sweep to close a connection before calling the sweeper dead.</summary>
	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

	/// <summary>A game, IRC or tournament connection is closed once it has sent nothing for 300 seconds.</summary>
	/// <param name="type">The kind of connection.</param>
	[Theory]
	[InlineData(ConnectionType.Bancho)]
	[InlineData(ConnectionType.Irc)]
	[InlineData(ConnectionType.Tourney)]
	public async Task ExecuteAsync_ClosesAnIdleGameIrcOrTourneyConnection(ConnectionType type)
	{
		using var app = new TestApplication();
		var permissions = type is ConnectionType.Tourney
			? UserPermissionsGroups.Default | UserPermissions.RoomObserve
			: UserPermissionsGroups.Default;
		var user = await app.CreateUserAsync("Idle", "password", permissions);
		var connection = await app.LoginAsync(user, type);
		app.Clock.Advance(TimeSpan.FromSeconds(301));
		var closed = NextCloseAsync(app, connection);
		using var running = await StartAsync(app);

		await SweepUntilAsync(app, closed);

		var @event = await closed;
		Assert.Equal(ConnectionCloseReason.TimedOut, @event.Reason);
		Assert.False(connection.IsOpen);
	}

	/// <summary>A connection that sent something within 300 seconds is still open.</summary>
	[Fact]
	public async Task ExecuteAsync_LeavesAConnectionThatJustSpokeOpen()
	{
		using var app = new TestApplication();
		var quietUser = await app.CreateUserAsync("Quiet", "password");
		var quiet = await app.LoginAsync(quietUser, ConnectionType.Bancho);
		var activeUser = await app.CreateUserAsync("Active", "password");
		var active = await app.LoginAsync(activeUser, ConnectionType.Bancho);
		app.Clock.Advance(TimeSpan.FromSeconds(301));
		app.Users.MarkActive(active);
		var quietClosed = NextCloseAsync(app, quiet);
		using var running = await StartAsync(app);

		await SweepUntilAsync(app, quietClosed);

		Assert.True(active.IsOpen);
	}

	/// <summary>An HTTP API connection is allowed to be quiet for two hours.</summary>
	[Fact]
	public async Task ExecuteAsync_LeavesAnApiConnectionQuietForLessThanTwoHoursOpen()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Api", "password");
		var connection = await app.LoginAsync(user, ConnectionType.Api);
		var canaryUser = await app.CreateUserAsync("Canary", "password");
		var canary = await app.LoginAsync(canaryUser, ConnectionType.Bancho);
		app.Clock.Advance(TimeSpan.FromSeconds(301));
		var canaryClosed = NextCloseAsync(app, canary);
		using var running = await StartAsync(app);

		await SweepUntilAsync(app, canaryClosed);

		Assert.True(connection.IsOpen);
	}

	/// <summary>An HTTP API connection is closed once it has been quiet for more than two hours.</summary>
	[Fact]
	public async Task ExecuteAsync_ClosesAnApiConnectionQuietForMoreThanTwoHours()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Api", "password");
		var connection = await app.LoginAsync(user, ConnectionType.Api);
		app.Clock.Advance(TimeSpan.FromHours(2) + TimeSpan.FromSeconds(1));
		var closed = NextCloseAsync(app, connection);
		using var running = await StartAsync(app);

		await SweepUntilAsync(app, closed);

		Assert.False(connection.IsOpen);
	}

	/// <summary>The server's own bot connection is never closed for being quiet.</summary>
	[Fact]
	public async Task ExecuteAsync_LeavesTheBotConnectionOpen()
	{
		using var app = new TestApplication();
		await app.SeedBotAsync();
		var bot = await app.Users.ConnectBotAsync(TestContext.Current.CancellationToken);
		Assert.NotNull(bot);
		var canaryUser = await app.CreateUserAsync("Canary", "password");
		var canary = await app.LoginAsync(canaryUser, ConnectionType.Bancho);
		app.Clock.Advance(TimeSpan.FromHours(3));
		var canaryClosed = NextCloseAsync(app, canary);
		using var running = await StartAsync(app);

		await SweepUntilAsync(app, canaryClosed);

		Assert.True(bot.IsOpen);
	}

	/// <summary>A connection closed once is not closed again by a later sweep.</summary>
	[Fact]
	public async Task ExecuteAsync_ClosesAConnectionOnlyOnce()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Once", "password");
		var first = await app.LoginAsync(user, ConnectionType.Bancho);
		var firstClosed = NextCloseAsync(app, first);
		using var running = await StartAsync(app);
		await SweepUntilAsync(app, firstClosed);

		var laterUser = await app.CreateUserAsync("Later", "password");
		var later = await app.LoginAsync(laterUser, ConnectionType.Bancho);
		var laterClosed = NextCloseAsync(app, later);
		await SweepUntilAsync(app, laterClosed);

		Assert.Single(app.UserEvents.Events.OfType<UserConnectionClosed>(),
			@event => ReferenceEquals(@event.Connection, first));
	}

	/// <summary>Runs the sweeper once the periodic timer it waits on exists.</summary>
	private static async Task<RunningSweeper> StartAsync(TestApplication app)
	{
		var time = new SignalingTimeProvider(app.Clock);
		var sweeper = new IdleConnectionSweeper(app.Users, time, NullLogger<IdleConnectionSweeper>.Instance);
		await sweeper.StartAsync(TestContext.Current.CancellationToken);
		await time.TimerCreated.WaitAsync(Patience, TestContext.Current.CancellationToken);
		return new RunningSweeper(sweeper);
	}

	/// <summary>Moves the clock one sweep at a time until the awaited change has happened.</summary>
	private static async Task SweepUntilAsync(TestApplication app, Task until)
	{
		for (var sweep = 0; sweep < 20 && !until.IsCompleted; sweep++)
		{
			app.Clock.Advance(SweepPeriod);
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

	/// <summary>The sweeper a test started, disposed after it stops.</summary>
	private sealed class RunningSweeper(IdleConnectionSweeper sweeper) : IDisposable
	{
		/// <inheritdoc />
		public void Dispose()
		{
			sweeper.Dispose();
		}
	}
}
