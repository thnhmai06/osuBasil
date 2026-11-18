using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Multiplayer.Registry;
using Basil.Infrastructure.Multiplayer.Workers;
using Basil.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Basil.Infrastructure.Multiplayer.Tests.Workers;

public sealed class EmptyRoomCloserTests
{
	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

	[Fact]
	public async Task ExecuteAsync_ClosesAnEmptyRoomAfterFifteenMinutes()
	{
		using var app = new TestApplication();
		var creator = await app.LoginAsync(await app.CreateUserAsync("Creator", "password"));
		var (room, opened) = await app.Lobby.ScopeTo(creator).OpenRoomAsync("empty room", "", RoomSource.Api, false);
		Assert.True(opened == RoomOutcome.Ok);
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

	private static async Task CheckUntilAsync(TestApplication app, TimeSpan span, Task until)
	{
		for (var moved = TimeSpan.Zero; moved < span && !until.IsCompleted; moved += EmptyRoomCloser.Period)
		{
			app.Clock.Advance(EmptyRoomCloser.Period);
			await Task.Yield();
		}
	}

	private sealed class RunningCloser(EmptyRoomCloser closer) : IDisposable
	{
		public void Dispose()
		{
			closer.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
			closer.Dispose();
		}
	}
}