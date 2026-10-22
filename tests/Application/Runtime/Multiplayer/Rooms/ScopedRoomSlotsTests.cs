using Basil.Application.Runtime.Multiplayer;
using Basil.Application.Runtime.Multiplayer.Events;
using Basil.Application.Runtime.Multiplayer.Rooms;
using Basil.Testing;
using Basil.Domain.Mechanics;

namespace Basil.Application.Runtime.Tests.Multiplayer.Rooms;

/// <summary>
///     The slots of a room. The expected behaviour comes from
///     docs/for-developers/multiplayer.md ("Membership, slots and settings"): there are sixteen numbered positions,
///     the room size counts the usable positions, and a locked room stops the players from changing slot or team.
/// </summary>
public sealed class ScopedRoomSlotsTests
{
	[Fact]
	public async Task ChangeAsync_ToAnOpenSlot_MovesThePlayer()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var from = setup.Room.Slots.Find(setup.Player)!.Index;

		var result = await setup.Room.ScopeTo(setup.Player).Slots.ChangeAsync(7,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Equal(7, setup.Room.Slots.Find(setup.Player)!.Index);
		Assert.Null(setup.Room.Slots.At(from)!.Player);
		var moved = Assert.IsType<RoomPlayerMoved>(app.RoomEvents.Events[^1]);
		Assert.Equal(from, moved.From);
		Assert.Equal(7, moved.To);
	}

	[Fact]
	public async Task ChangeAsync_ToAnOpenSlot_CarriesTheReadyStateToTheNewSlot()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		Assert.True((await setup.Room.ScopeTo(setup.Player).Slots.SetReadyAsync(true,
			TestContext.Current.CancellationToken)).IsOk);

		await setup.Room.ScopeTo(setup.Player).Slots.ChangeAsync(7, TestContext.Current.CancellationToken);

		Assert.Equal(RoomSlotStatus.Ready, setup.Room.Slots.Find(setup.Player)!.Status);
	}

	[Fact]
	public async Task ChangeAsync_ToTheSameSlot_ChangesNothing()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var events = app.RoomEvents.Events.Count;

		var result = await setup.Room.ScopeTo(setup.Player).Slots.ChangeAsync(3,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Equal(3, setup.Room.Slots.Find(setup.Player)!.Index);
		Assert.Equal(events, app.RoomEvents.Events.Count);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(17)]
	public async Task ChangeAsync_ToASlotOutsideTheRoom_IsRefused(int index)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Player).Slots.ChangeAsync(index,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.SlotNotOpen, result.Outcome);
	}

	[Fact]
	public async Task ChangeAsync_ToALockedSlot_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		Assert.True((await setup.Room.ScopeTo(setup.Host).Slots.ToggleLockAsync(7,
			TestContext.Current.CancellationToken)).IsOk);

		var result = await setup.Room.ScopeTo(setup.Player).Slots.ChangeAsync(7,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.SlotNotOpen, result.Outcome);
		Assert.Equal(3, setup.Room.Slots.Find(setup.Player)!.Index);
	}

	[Fact]
	public async Task ChangeAsync_ToAnOccupiedSlot_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Player).Slots.ChangeAsync(2,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.SlotNotOpen, result.Outcome);
		Assert.Equal(3, setup.Room.Slots.Find(setup.Player)!.Index);
	}

	[Fact]
	public async Task ChangeAsync_WhileTheRoomIsLocked_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		Assert.True((await setup.Room.ScopeTo(setup.Creator).Settings.SetLockedAsync(true,
			TestContext.Current.CancellationToken)).IsOk);

		var result = await setup.Room.ScopeTo(setup.Player).Slots.ChangeAsync(7,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.RoomLocked, result.Outcome);
		Assert.Equal(3, setup.Room.Slots.Find(setup.Player)!.Index);
	}

	[Fact]
	public async Task ChangeAsync_ForSomebodyWhoIsNotSeated_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Outsider).Slots.ChangeAsync(7,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.NotInRoom, result.Outcome);
	}

	[Fact]
	public async Task MoveAsync_ToAnOccupiedSlot_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Slots.MoveAsync(setup.Player.Record, 2,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.SlotNotOpen, result.Outcome);
		Assert.Equal(3, setup.Room.Slots.Find(setup.Player)!.Index);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(17)]
	public async Task MoveAsync_ToASlotOutsideTheRoom_IsRefused(int index)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Slots.MoveAsync(setup.Player.Record, index,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.SlotNotOpen, result.Outcome);
	}

	[Fact]
	public async Task MoveAsync_WhileTheRoomIsLocked_StillMovesThePlayer()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		Assert.True((await setup.Room.ScopeTo(setup.Creator).Settings.SetLockedAsync(true,
			TestContext.Current.CancellationToken)).IsOk);

		var result = await setup.Room.ScopeTo(setup.Creator).Slots.MoveAsync(setup.Player.Record, 7,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Equal(7, setup.Room.Slots.Find(setup.Player)!.Index);
	}

	[Fact]
	public async Task ToggleLockAsync_ForTheCallersOwnSlot_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var own = setup.Room.Slots.Find(setup.Host)!.Index;

		var result = await setup.Room.ScopeTo(setup.Host).Slots.ToggleLockAsync(own,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.OwnSlot, result.Outcome);
		Assert.False(setup.Room.Slots.At(own)!.Locked);
	}

	[Fact]
	public async Task ToggleLockAsync_ForAnOccupiedSlot_EvictsThePlayer()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var occupied = setup.Room.Slots.Find(setup.Player)!.Index;

		var result = await setup.Room.ScopeTo(setup.Host).Slots.ToggleLockAsync(occupied,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.True(setup.Room.Slots.At(occupied)!.Locked);
		Assert.Null(setup.Room.Slots.Find(setup.Player));
		var locked = Assert.IsType<RoomSlotLockChanged>(app.RoomEvents.Events[^1]);
		Assert.Same(setup.Player, locked.Evicted);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(17)]
	public async Task ToggleLockAsync_ForASlotOutsideTheRoom_IsRefused(int index)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Host).Slots.ToggleLockAsync(index,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.SlotNotOpen, result.Outcome);
	}

	[Fact]
	public async Task ConfigureAsync_ResizingTheRoom_KeepsThePlayersWhereTheySit()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Size: 5), TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Equal(1, setup.Room.Slots.Find(setup.Creator)!.Index);
		Assert.Equal(2, setup.Room.Slots.Find(setup.Host)!.Index);
		Assert.Equal(3, setup.Room.Slots.Find(setup.Player)!.Index);
		Assert.False(setup.Room.Slots.At(4)!.Locked);
		Assert.False(setup.Room.Slots.At(5)!.Locked);
		Assert.True(setup.Room.Slots.At(6)!.Locked);
	}

	[Fact]
	public async Task SetReadyAsync_ForAPlayer_SetsThemReady()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Player).Slots.SetReadyAsync(true,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Equal(RoomSlotStatus.Ready, setup.Room.Slots.Find(setup.Player)!.Status);
		var changed = Assert.IsType<RoomSlotStatusChanged>(app.RoomEvents.Events[^1]);
		Assert.Equal(RoomSlotStatus.Ready, changed.Status);
	}

	[Fact]
	public async Task SetReadyAsync_WhileARoundIsInProgress_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Beatmap: RoomFixture.Beatmap), TestContext.Current.CancellationToken);
		await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync(TestContext.Current.CancellationToken);

		var result = await setup.Room.ScopeTo(setup.Player).Slots.SetReadyAsync(false,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.InProgress, result.Outcome);
	}

	[Fact]
	public async Task SetHasMapAsync_WithoutTheMap_SetsTheSlotToNoMap()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Player).Slots.SetHasMapAsync(false,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Equal(RoomSlotStatus.NoMap, setup.Room.Slots.Find(setup.Player)!.Status);
		Assert.IsType<RoomSlotStatusChanged>(app.RoomEvents.Events[^1]);
	}

	[Fact]
	public async Task ToggleTeamAsync_InARoomWithoutTeams_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Player).Slots.ToggleTeamAsync(
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.NoTeams, result.Outcome);
	}

	[Fact]
	public async Task ToggleTeamAsync_WhileTheRoomIsLocked_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(TeamType: GameTeamType.TeamVs), TestContext.Current.CancellationToken);
		Assert.True((await setup.Room.ScopeTo(setup.Creator).Settings.SetLockedAsync(true,
			TestContext.Current.CancellationToken)).IsOk);

		var result = await setup.Room.ScopeTo(setup.Player).Slots.ToggleTeamAsync(
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.RoomLocked, result.Outcome);
	}

	[Fact]
	public async Task SetModsAsync_WithoutFreemod_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Player).Slots.SetModsAsync(GameMods.Hidden,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.NotFreemod, result.Outcome);
	}

	[Fact]
	public async Task SetModsAsync_WithASpeedChangingMod_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Freemods: true), TestContext.Current.CancellationToken);

		var result = await setup.Room.ScopeTo(setup.Player).Slots.SetModsAsync(GameMods.DoubleTime,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.SpeedModNotAllowed, result.Outcome);
	}

	[Fact]
	public async Task SetModsAsync_WithAModTheModeDoesNotAllow_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Freemods: true), TestContext.Current.CancellationToken);

		var result = await setup.Room.ScopeTo(setup.Player).Slots.SetModsAsync(GameMods.Key1,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.InvalidMods, result.Outcome);
	}

	[Fact]
	public async Task ArrangeAsync_WithAnArrangementThatDoesNotHoldEveryPlayer_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var arrangement = new[] { new SlotArrangement(1, setup.Creator.Record, null, false) };

		var result = await setup.Room.ScopeTo(setup.Creator).Slots.ArrangeAsync(arrangement,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.InvalidSettings, result.Outcome);
		Assert.Equal(3, setup.Room.Slots.Find(setup.Player)!.Index);
	}

	[Fact]
	public async Task ChangeAsync_WhileARoundIsInProgress_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Beatmap: RoomFixture.Beatmap), TestContext.Current.CancellationToken);
		await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync(TestContext.Current.CancellationToken);

		var result = await setup.Room.ScopeTo(setup.Player).Slots.ChangeAsync(7,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.InProgress, result.Outcome);
		Assert.Equal(3, setup.Room.Slots.Find(setup.Player)!.Index);
	}

	[Fact]
	public async Task SetModsAsync_UnderFreemod_SetsThePlayersMods()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Freemods: true), TestContext.Current.CancellationToken);

		var result = await setup.Room.ScopeTo(setup.Player).Slots.SetModsAsync(GameMods.Hidden,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Equal(GameMods.Hidden, setup.Room.Slots.Find(setup.Player)!.Mods);
		Assert.IsType<RoomSlotModsChanged>(app.RoomEvents.Events[^1]);
	}
}