using Basil.Application.Runtime.Multiplayer;
using Basil.Application.Runtime.Tests.Support;

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
			setup.Room.ScopeTo(first).Members.JoinAsync("", TestContext.Current.CancellationToken),
			setup.Room.ScopeTo(second).Members.JoinAsync("", TestContext.Current.CancellationToken));

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
			setup.Room.ScopeTo(setup.Creator).Authority.SetHostAsync(setup.Host,
				TestContext.Current.CancellationToken),
			setup.Room.ScopeTo(setup.Creator).Authority.SetHostAsync(setup.Player,
				TestContext.Current.CancellationToken));

		Assert.All(results, result => Assert.Equal(RoomOutcome.Ok, result.Outcome));
		var host = setup.Room.Authority.Host;
		Assert.True(ReferenceEquals(host, setup.Host) || ReferenceEquals(host, setup.Player));
		Assert.Same(host, setup.Room.Slots.Find(host)!.Player);
	}
}