using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Multiplayer.Registry;
using Basil.Infrastructure.Multiplayer.Workers;
using Basil.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Basil.Infrastructure.Multiplayer.Tests.Workers;

/// <summary>
///     Pins that a room closes by itself once it stayed empty for fifteen minutes; which rooms count as empty is
///     pinned by the lobby's own tests.
/// </summary>
public sealed class EmptyRoomCloserTests
{
	/// <summary>How long a test waits for the worker to close a room before calling it dead.</summary>
	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

	/// <summary>A room nobody sits in closes once the empty period has passed, and not a check earlier.</summary>
	[Fact]
	public async Task ExecuteAsync_ClosesAnEmptyRoomAfterFifteenMinutes()
	{
		using var app = new TestApplication();
		var creator = await app.LoginAsync(await app.CreateUserAsync("Creator", "password"));
		var (room, opened) = await app.Lobby.ScopeTo(creator).OpenRoomAsync("empty room", "", RoomSource.Api, false,
			cancellationToken: TestContext.Current.CancellationToken);
		Assert.True(opened.IsOk);
		var closed = NextClose(app, room!);
		using var running = await StartAsync(app);

		await CheckUntilAsync(app, Lobby.EmptyRoomTimeout - EmptyRoomCloser.Period, closed);
		Assert.False(room!.IsClosed);

		await CheckUntilAsync(app, EmptyRoomCloser.Period * 2, closed);

		var close = await closed.WaitAsync(Patience, TestContext.Current.CancellationToken);
		Assert.True(room.IsClosed);
		Assert.Null(close.Actor);
	}

	private static Task<LobbyRoomClosed> NextClose(TestApplication app, Room room)
	{
		var closed = new TaskCompletionSource<LobbyRoomClosed>(TaskCreationOptions.RunContinuationsAsynchronously);
		app.LobbyEvents.Recorded += @event =>
		{
			if (@event is LobbyRoomClosed close && ReferenceEquals(close.Room, room)) closed.TrySetResult(close);
		};
		return closed.Task;
	}

	private static async Task<RunningCloser> StartAsync(TestApplication app)
	{
		var time = new SignalingTimeProvider(app.Clock);
		var closer = new EmptyRoomCloser(app.Lobby, time, NullLogger<EmptyRoomCloser>.Instance);
		await closer.StartAsync(TestContext.Current.CancellationToken);
		await time.TimerCreated.WaitAsync(Patience, TestContext.Current.CancellationToken);
		return new RunningCloser(closer);
	}

	/// <summary>Moves the clock one check at a time over a span, stopping early once the awaited close happened.</summary>
	private static async Task CheckUntilAsync(TestApplication app, TimeSpan span, Task until)
	{
		for (var moved = TimeSpan.Zero; moved < span && !until.IsCompleted; moved += EmptyRoomCloser.Period)
		{
			app.Clock.Advance(EmptyRoomCloser.Period);
			await Task.Yield();
		}
	}

	/// <summary>The worker a test started, disposed after it stops.</summary>
	private sealed class RunningCloser(EmptyRoomCloser closer) : IDisposable
	{
		public void Dispose()
		{
			closer.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
			closer.Dispose();
		}
	}
}