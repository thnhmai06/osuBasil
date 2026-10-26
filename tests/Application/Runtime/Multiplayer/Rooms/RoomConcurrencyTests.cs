using Basil.Application.Runtime.Multiplayer;
using Basil.Application.Runtime.Multiplayer.Events;
using Basil.Testing;

namespace Basil.Application.Runtime.Tests.Multiplayer.Rooms;

/// <summary>
///     What happens when two callers act on a room at the same time. The room admits one state transition at a
///     time (docs/for-developers/multiplayer.md, "Room lifecycle and concurrency"), so the outcome of a race is
///     decided by the room and not by the order in which the callers happen to arrive.
/// </summary>
public sealed class RoomConcurrencyTests
{
	[Fact]
	public async Task JoinAsync_TwoPlayersRacingForTheLastSlot_SeatsOneAndRefusesTheOther()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Size: 4), TestContext.Current.CancellationToken);
		var first = await RoomFixture.PlayerAsync(app, "First");
		var second = await RoomFixture.PlayerAsync(app, "Second");

		var results = await Task.WhenAll(
			Task.Run(() => setup.Room.ScopeTo(first).Members.JoinAsync("", TestContext.Current.CancellationToken)),
			Task.Run(() => setup.Room.ScopeTo(second).Members.JoinAsync("", TestContext.Current.CancellationToken)));

		Assert.Equal(1, results.Count(result => result.Outcome == RoomOutcome.Ok));
		Assert.Equal(1, results.Count(result => result.Outcome == RoomOutcome.Full));
		Assert.Equal(1, setup.Room.Slots.Count(slot => slot.Player is not null && slot.Index == 4));
	}

	[Fact]
	public async Task SetHostAsync_TwoCallersRacing_LeavesOneSeatedHost()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var results = await Task.WhenAll(
			Task.Run(() => setup.Room.ScopeTo(setup.Creator).Authority.SetHostAsync(setup.Host,
				TestContext.Current.CancellationToken)),
			Task.Run(() => setup.Room.ScopeTo(setup.Creator).Authority.SetHostAsync(setup.Player,
				TestContext.Current.CancellationToken)));

		Assert.All(results, result => Assert.Equal(RoomOutcome.Ok, result.Outcome));
		var host = setup.Room.Authority.Host;
		Assert.True(ReferenceEquals(host, setup.Host) || ReferenceEquals(host, setup.Player));
		Assert.Same(host, setup.Room.Slots.Find(host)!.Player);
	}

	[Fact]
	public async Task JoinAsync_OnePlayerRacingForTwoRooms_IsSeatedInExactlyOne()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		for (var attempt = 0; attempt < 200; attempt++)
		{
			var racer = await RoomFixture.PlayerAsync(app, $"Racer{attempt}");

			var results = await Task.WhenAll(
				Task.Run(() => setup.Room.ScopeTo(racer).Members.JoinAsync("", TestContext.Current.CancellationToken)),
				Task.Run(() => setup.SuspendedRoom.ScopeTo(racer).Members
					.JoinAsync("", TestContext.Current.CancellationToken)));

			Assert.Equal(1, results.Count(result => result.Outcome == RoomOutcome.Ok));
			Assert.Equal(1,
				new[] { setup.Room, setup.SuspendedRoom }.Count(room => room.Slots.Find(racer) is not null));

			await app.Lobby.ReleaseAsync(racer, TestContext.Current.CancellationToken);
		}
	}

	[Fact]
	public async Task StartAsync_TwoCallersRacing_StartsOneRound()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Beatmap: RoomFixture.Beatmap), TestContext.Current.CancellationToken);

		var results = await Task.WhenAll(
			Task.Run(() => setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync(TestContext.Current.CancellationToken)),
			Task.Run(() => setup.Room.ScopeTo(setup.Referee).Rounds.StartAsync(TestContext.Current.CancellationToken)));

		Assert.Equal(1, results.Count(result => result.Outcome == RoomOutcome.Ok));
		Assert.Equal(1, results.Count(result => result.Outcome == RoomOutcome.InProgress));
		Assert.Equal(1, app.RoomEvents.Events.OfType<RoomRoundStarted>().Count());
	}

	[Fact]
	public async Task JoinAsync_RacingTheRoomsClosing_LeavesNobodySeatedInAClosedRoom()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		for (var attempt = 0; attempt < 100; attempt++)
		{
			var owner = await RoomFixture.PlayerAsync(app, $"Owner{attempt}");
			var (room, opened) = await app.Lobby.ScopeTo(owner).OpenRoomAsync($"race {attempt}", "", false, false,
				cancellationToken: TestContext.Current.CancellationToken);
			Assert.True(opened.IsOk);
			var joiner = await RoomFixture.PlayerAsync(app, $"Joiner{attempt}");

			var results = await Task.WhenAll(
				Task.Run(() => room!.ScopeTo(joiner).Members.JoinAsync("", TestContext.Current.CancellationToken)),
				Task.Run(() => room!.ScopeTo(owner).CloseAsync(TestContext.Current.CancellationToken)));

			Assert.Contains(results[0].Outcome, new[] { RoomOutcome.Ok, RoomOutcome.RoomClosed });
			Assert.True(room!.IsClosed);
			Assert.Null(room.Slots.Find(joiner));
			Assert.Null(app.Lobby.RoomOf(joiner));
			Assert.True((await setup.SuspendedRoom.ScopeTo(joiner).Members
				.JoinAsync("", TestContext.Current.CancellationToken)).IsOk);
			await app.Lobby.ReleaseAsync(joiner, TestContext.Current.CancellationToken);
		}
	}
}