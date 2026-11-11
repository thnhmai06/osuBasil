using Basil.Application.Multiplayer.Objects;
using Basil.Application.Users.Objects;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Domain.Mechanics;
using Basil.Testing;

namespace Basil.Application.Multiplayer.Tests.Capabilities.Rooms;

/// <summary>
///     The slots of a room. The expected behaviour comes from
///     docs/for-developers/multiplayer.md ("Membership, slots and settings"): there are sixteen numbered positions,
///     the room size counts the usable positions, and a locked room stops the players from changing slot or team.
/// </summary>
public sealed class ScopedRoomSlotsTests
{
	[Fact]
	public async Task OpenSlot_MovesPlayer()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var from = setup.Room.Slots.Find(setup.Player)!.Index;

		var result = await setup.Room.ScopeTo(setup.Player).Slots.ChangeAsync(7);

		Assert.Equal(RoomOutcome.Ok, result);
		Assert.Equal(7, setup.Room.Slots.Find(setup.Player)!.Index);
		Assert.Null(setup.Room.Slots.At(from)!.Player);
		var moved = Assert.IsType<RoomPlayerMoved>(app.RoomEvents.Events[^1]);
		Assert.Equal(from, moved.From);
		Assert.Equal(7, moved.To);
	}

	[Fact]
	public async Task OpenSlot_KeepsReadyState()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		Assert.True((await setup.Room.ScopeTo(setup.Player).Slots.SetReadyAsync(true)) == RoomOutcome.Ok);

		await setup.Room.ScopeTo(setup.Player).Slots.ChangeAsync(7);

		Assert.Equal(RoomSlotStatus.Ready, setup.Room.Slots.Find(setup.Player)!.Status);
	}

	[Fact]
	public async Task SameSlot_ChangesNothing()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var events = app.RoomEvents.Events.Count;

		var result = await setup.Room.ScopeTo(setup.Player).Slots.ChangeAsync(3);

		Assert.Equal(RoomOutcome.Ok, result);
		Assert.Equal(3, setup.Room.Slots.Find(setup.Player)!.Index);
		Assert.Equal(events, app.RoomEvents.Events.Count);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(17)]
	public async Task ChangingOutsideSlot_Refused(int index)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Player).Slots.ChangeAsync(index);

		Assert.Equal(RoomOutcome.SlotNotOpen, result);
	}

	[Fact]
	public async Task LockedSlot_Refused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		Assert.True((await setup.Room.ScopeTo(setup.Host).Slots.ToggleLockAsync(7)) == RoomOutcome.Ok);

		var result = await setup.Room.ScopeTo(setup.Player).Slots.ChangeAsync(7);

		Assert.Equal(RoomOutcome.SlotNotOpen, result);
		Assert.Equal(3, setup.Room.Slots.Find(setup.Player)!.Index);
	}

	[Fact]
	public async Task ChangingOccupiedSlot_Refused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Player).Slots.ChangeAsync(2);

		Assert.Equal(RoomOutcome.SlotNotOpen, result);
		Assert.Equal(3, setup.Room.Slots.Find(setup.Player)!.Index);
	}

	[Fact]
	public async Task LockedRoomChange_Refused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		Assert.True((await setup.Room.ScopeTo(setup.Creator).Settings.SetLockedAsync(true)) == RoomOutcome.Ok);

		var result = await setup.Room.ScopeTo(setup.Player).Slots.ChangeAsync(7);

		Assert.Equal(RoomOutcome.RoomLocked, result);
		Assert.Equal(3, setup.Room.Slots.Find(setup.Player)!.Index);
	}

	[Fact]
	public async Task UnseatedPlayer_Refused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Outsider).Slots.ChangeAsync(7);

		Assert.Equal(RoomOutcome.NotInRoom, result);
	}

	[Fact]
	public async Task MovingOccupiedSlot_Refused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Slots.MoveAsync(UserReference.ById(setup.Player.Record.Id), 2);

		Assert.Equal(RoomOutcome.SlotNotOpen, result);
		Assert.Equal(3, setup.Room.Slots.Find(setup.Player)!.Index);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(17)]
	public async Task MovingOutsideSlot_Refused(int index)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Slots.MoveAsync(UserReference.ById(setup.Player.Record.Id), index);

		Assert.Equal(RoomOutcome.SlotNotOpen, result);
	}

	[Fact]
	public async Task LockedRoomMove_StillMoves()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		Assert.True((await setup.Room.ScopeTo(setup.Creator).Settings.SetLockedAsync(true)) == RoomOutcome.Ok);

		var result = await setup.Room.ScopeTo(setup.Creator).Slots.MoveAsync(UserReference.ById(setup.Player.Record.Id), 7);

		Assert.Equal(RoomOutcome.Ok, result);
		Assert.Equal(7, setup.Room.Slots.Find(setup.Player)!.Index);
	}

	[Fact]
	public async Task OwnSlotLock_Refused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var own = setup.Room.Slots.Find(setup.Host)!.Index;

		var result = await setup.Room.ScopeTo(setup.Host).Slots.ToggleLockAsync(own);

		Assert.Equal(RoomOutcome.OwnSlot, result);
		Assert.False(setup.Room.Slots.At(own)!.Locked);
	}

	[Fact]
	public async Task OccupiedSlotLock_Evicts()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var occupied = setup.Room.Slots.Find(setup.Player)!.Index;

		var result = await setup.Room.ScopeTo(setup.Host).Slots.ToggleLockAsync(occupied);

		Assert.Equal(RoomOutcome.Ok, result);
		Assert.True(setup.Room.Slots.At(occupied)!.Locked);
		Assert.Null(setup.Room.Slots.Find(setup.Player));
		var locked = Assert.IsType<RoomSlotLockChanged>(app.RoomEvents.Events[^1]);
		Assert.Same(setup.Player, locked.Evicted);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(17)]
	public async Task LockingOutsideSlot_Refused(int index)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Host).Slots.ToggleLockAsync(index);

		Assert.Equal(RoomOutcome.SlotNotOpen, result);
	}

	[Fact]
	public async Task RoomResized_KeepsPlayerSlots()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Size: 5));

		Assert.Equal(RoomOutcome.Ok, result);
		Assert.Equal(1, setup.Room.Slots.Find(setup.Creator)!.Index);
		Assert.Equal(2, setup.Room.Slots.Find(setup.Host)!.Index);
		Assert.Equal(3, setup.Room.Slots.Find(setup.Player)!.Index);
		Assert.False(setup.Room.Slots.At(4)!.Locked);
		Assert.False(setup.Room.Slots.At(5)!.Locked);
		Assert.True(setup.Room.Slots.At(6)!.Locked);
	}

	[Fact]
	public async Task PlayerReady_MarkedReady()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Player).Slots.SetReadyAsync(true);

		Assert.Equal(RoomOutcome.Ok, result);
		Assert.Equal(RoomSlotStatus.Ready, setup.Room.Slots.Find(setup.Player)!.Status);
		var changed = Assert.IsType<RoomSlotStatusChanged>(app.RoomEvents.Events[^1]);
		Assert.Equal(RoomSlotStatus.Ready, changed.Status);
	}

	[Fact]
	public async Task ReadyDuringRound_Refused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Beatmap: RoomFixture.Beatmap));
		await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync();

		var result = await setup.Room.ScopeTo(setup.Player).Slots.SetReadyAsync(false);

		Assert.Equal(RoomOutcome.InProgress, result);
	}

	[Fact]
	public async Task MissingMap_SlotCleared()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Player).Slots.SetHasMapAsync(false);

		Assert.Equal(RoomOutcome.Ok, result);
		Assert.Equal(RoomSlotStatus.NoMap, setup.Room.Slots.Find(setup.Player)!.Status);
		Assert.IsType<RoomSlotStatusChanged>(app.RoomEvents.Events[^1]);
	}

	[Fact]
	public async Task TeamlessRoom_Refused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Player).Slots.ToggleTeamAsync();

		Assert.Equal(RoomOutcome.NoTeams, result);
	}

	[Fact]
	public async Task LockedTeamToggle_Refused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(TeamType: GameTeamType.TeamVs));
		Assert.True((await setup.Room.ScopeTo(setup.Creator).Settings.SetLockedAsync(true)) == RoomOutcome.Ok);

		var result = await setup.Room.ScopeTo(setup.Player).Slots.ToggleTeamAsync();

		Assert.Equal(RoomOutcome.RoomLocked, result);
	}

	[Fact]
	public async Task NoFreemod_Refused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Player).Slots.SetModsAsync(GameMods.Hidden);

		Assert.Equal(RoomOutcome.NotFreemod, result);
	}

	[Fact]
	public async Task SpeedChangingMod_Refused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Freemods: true));

		var result = await setup.Room.ScopeTo(setup.Player).Slots.SetModsAsync(GameMods.DoubleTime);

		Assert.Equal(RoomOutcome.SpeedModNotAllowed, result);
	}

	[Fact]
	public async Task ModeDisallowedMod_Refused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Freemods: true));

		var result = await setup.Room.ScopeTo(setup.Player).Slots.SetModsAsync(GameMods.Key1);

		Assert.Equal(RoomOutcome.InvalidMods, result);
	}

	[Fact]
	public async Task ArrangementTooSmall_Refused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var arrangement = new[] { new SlotArrangement(1, setup.Creator.Record, null, false) };

		var result = await setup.Room.ScopeTo(setup.Creator).Slots.ArrangeAsync(arrangement);

		Assert.Equal(RoomOutcome.InvalidSettings, result);
		Assert.Equal(3, setup.Room.Slots.Find(setup.Player)!.Index);
	}

	[Fact]
	public async Task ChangingDuringRound_Refused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Beatmap: RoomFixture.Beatmap));
		await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync();

		var result = await setup.Room.ScopeTo(setup.Player).Slots.ChangeAsync(7);

		Assert.Equal(RoomOutcome.InProgress, result);
		Assert.Equal(3, setup.Room.Slots.Find(setup.Player)!.Index);
	}

	[Fact]
	public async Task FreemodActive_SetsPlayerMods()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Freemods: true));

		var result = await setup.Room.ScopeTo(setup.Player).Slots.SetModsAsync(GameMods.Hidden);

		Assert.Equal(RoomOutcome.Ok, result);
		Assert.Equal(GameMods.Hidden, setup.Room.Slots.Find(setup.Player)!.Mods);
		Assert.IsType<RoomSlotModsChanged>(app.RoomEvents.Events[^1]);
	}
}