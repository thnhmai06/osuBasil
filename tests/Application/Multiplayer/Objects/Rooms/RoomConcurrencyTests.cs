using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Testing;

namespace Basil.Application.Multiplayer.Tests.Objects.Rooms;

/// <summary>
///     What happens when two callers act on a room at the same time. The room admits one state transition at a
///     time (docs/for-developers/multiplayer.md, "Room lifecycle and concurrency"), so the outcome of a race is
///     decided by the room and not by the order in which the callers happen to arrive.
/// </summary>
public sealed class RoomConcurrencyTests
{
	[Fact]
	public async Task LastSlotRace_SeatsOne()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Size: 4));
		var first = await RoomFixture.PlayerAsync(app, "First");
		var second = await RoomFixture.PlayerAsync(app, "Second");

		var results = await Task.WhenAll(
			Task.Run(() => setup.Room.ScopeTo(first).Members.JoinAsync("")),
			Task.Run(() => setup.Room.ScopeTo(second).Members.JoinAsync("")));

		Assert.Equal(1, results.Count(result => result == RoomOutcome.Ok));
		Assert.Equal(1, results.Count(result => result == RoomOutcome.Full));
		Assert.Equal(1, setup.Room.Slots.Count(slot => slot.Player is not null && slot.Index == 4));
	}

	[Fact]
	public async Task HostRace_LeavesOneHost()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var results = await Task.WhenAll(
			Task.Run(() => setup.Room.ScopeTo(setup.Creator).Authority.SetHostAsync(setup.Host)),
			Task.Run(() => setup.Room.ScopeTo(setup.Creator).Authority.SetHostAsync(setup.Player)));

		Assert.All(results, result => Assert.Equal(RoomOutcome.Ok, result));
		var host = setup.Room.Authority.Host;
		Assert.True(ReferenceEquals(host, setup.Host) || ReferenceEquals(host, setup.Player));
		Assert.Same(host, setup.Room.Slots.Find(host)!.Player);
	}

	[Fact]
	public async Task RacingTwoRooms_SeatedOnce()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		for (var attempt = 0; attempt < 200; attempt++)
		{
			var racer = await RoomFixture.PlayerAsync(app, $"Racer{attempt}");

			var results = await Task.WhenAll(
				Task.Run(() => setup.Room.ScopeTo(racer).Members.JoinAsync("")),
				Task.Run(() => setup.SuspendedRoom.ScopeTo(racer).Members
					.JoinAsync("")));

			Assert.Equal(1, results.Count(result => result == RoomOutcome.Ok));
			Assert.Equal(1,
				new[] { setup.Room, setup.SuspendedRoom }.Count(room => room.Slots.Find(racer) is not null));

			await app.Lobby.ReleaseAsync(racer);
		}
	}

	[Fact]
	public async Task StartRace_StartsOneRound()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Beatmap: RoomFixture.Beatmap));

		var results = await Task.WhenAll(
			Task.Run(() => setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync()),
			Task.Run(() => setup.Room.ScopeTo(setup.Referee).Rounds.StartAsync()));

		Assert.Equal(1, results.Count(result => result == RoomOutcome.Ok));
		Assert.Equal(1, results.Count(result => result == RoomOutcome.InProgress));
		Assert.Single(app.RoomEvents.Events.OfType<RoomRoundStarted>());
	}

	[Fact]
	public async Task JoinDuringClose_SeatsNobody()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		for (var attempt = 0; attempt < 100; attempt++)
		{
			var owner = await RoomFixture.PlayerAsync(app, $"Owner{attempt}");
			var (room, opened) = await app.Lobby.ScopeTo(owner).OpenRoomAsync($"race {attempt}", "", RoomSource.Game,
				false);
			Assert.True(opened == RoomOutcome.Ok);
			var joiner = await RoomFixture.PlayerAsync(app, $"Joiner{attempt}");

			var results = await Task.WhenAll(
				Task.Run(() => room!.ScopeTo(joiner).Members.JoinAsync("")),
				Task.Run(() => room!.ScopeTo(owner).CloseAsync()));

			Assert.Contains(results[0], new[] { RoomOutcome.Ok, RoomOutcome.RoomClosed });
			Assert.True(room!.IsClosed);
			Assert.Null(room.Slots.Find(joiner));
			Assert.Null(app.Lobby.RoomOf(joiner));
			Assert.True(await setup.SuspendedRoom.ScopeTo(joiner).Members
				.JoinAsync("") == RoomOutcome.Ok);
			await app.Lobby.ReleaseAsync(joiner);
		}
	}
}