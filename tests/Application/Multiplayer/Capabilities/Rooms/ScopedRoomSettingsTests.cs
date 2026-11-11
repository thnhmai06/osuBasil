using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Domain.Mechanics;
using Basil.Testing;

namespace Basil.Application.Multiplayer.Tests.Capabilities.Rooms;

/// <summary>
///     The settings of a room. The expected behaviour comes from docs/for-developers/multiplayer.md ("Membership,
///     slots and settings"): a room's settings change as one validated operation, settings cannot change during a
///     round, changing the team type reassigns teams and changing the mode drops the mods that mode does not allow.
/// </summary>
public sealed class ScopedRoomSettingsTests
{
	[Fact]
	public async Task ConfigureAsync_ChangesTheName()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange("renamed"));

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Equal("renamed", setup.Room.Settings.Name);
		var changed = Assert.Single(app.RoomEvents.Events.OfType<RoomSettingsChanged>());
		Assert.Equal("renamed", changed.Change.Name);
	}

	[Fact]
	public async Task ConfigureAsync_PasswordHidden()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Password: "secret"));

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Equal("secret", setup.Room.Settings.Password);
		var changed = Assert.Single(app.RoomEvents.Events.OfType<RoomSettingsChanged>());
		Assert.True(changed.PasswordChanged);
		Assert.Null(changed.Change.Password);
	}

	[Fact]
	public async Task ConfigureEmptyPassword_Removed()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Password: "secret"));

		var result = await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Password: ""));

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Equal("", setup.Room.Settings.Password);
	}

	[Fact]
	public async Task ConfigureAsync_ChangesTheMode()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Mode: GameMode.Mania));

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Equal(GameMode.Mania, setup.Room.Settings.MatchSettings.Mode);
	}

	[Fact]
	public async Task ModeChange_ModsDropped()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Mods: GameMods.Autopilot));
		Assert.Equal(GameMods.Autopilot, setup.Room.Settings.MatchSettings.Mods);

		var result = await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Mode: GameMode.Mania));

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Equal(GameMods.NoMod, setup.Room.Settings.MatchSettings.Mods);
	}

	[Fact]
	public async Task ConfigureAsync_WinCondition()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(WinCondition: GameWinCondition.Accuracy));

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Equal(GameWinCondition.Accuracy, setup.Room.Settings.MatchSettings.WinCondition);
	}

	[Fact]
	public async Task ConfigureTeamType_TeamsReassigned()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(TeamType: GameTeamType.TeamVs));

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Equal(GameTeamType.TeamVs, setup.Room.Settings.MatchSettings.TeamType);
		Assert.Equal(GameTeam.Red, setup.Room.Slots.Find(setup.Creator)!.Team);
		Assert.Equal(GameTeam.Blue, setup.Room.Slots.Find(setup.Host)!.Team);
	}

	[Fact]
	public async Task ConfigureBeatmap_ReadyReset()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		Assert.True((await setup.Room.ScopeTo(setup.Player).Slots.SetReadyAsync(true)).IsOk);

		var result = await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Beatmap: RoomFixture.Beatmap));

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Equal(RoomFixture.Beatmap, setup.Room.Settings.Beatmap);
		Assert.Equal(RoomSlotStatus.NotReady, setup.Room.Slots.Find(setup.Player)!.Status);
	}

	[Fact]
	public async Task ConfigureFreemodOn_ModsToPlayers()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Mods: GameMods.Hidden));

		var result = await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Freemods: true));

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Equal(GameMods.NoMod, setup.Room.Settings.MatchSettings.Mods);
		Assert.Equal(GameMods.Hidden, setup.Room.Slots.Find(setup.Player)!.Mods);
	}

	[Fact]
	public async Task ConfigureFreemodOff_ModsToRoom()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Mods: GameMods.Hidden));
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Freemods: true));

		var result = await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Freemods: false));

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Equal(GameMods.Hidden, setup.Room.Settings.MatchSettings.Mods);
		Assert.Equal(GameMods.NoMod, setup.Room.Slots.Find(setup.Player)!.Mods);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("bad\0name")]
	public async Task ConfigureInvalidName_Refused(string name)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(name));

		Assert.Equal(RoomOutcome.InvalidSettings, result.Outcome);
		Assert.NotEqual(name, setup.Room.Settings.Name);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(17)]
	public async Task ConfigureInvalidSize_Refused(int size)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Size: size));

		Assert.Equal(RoomOutcome.InvalidSettings, result.Outcome);
	}

	[Fact]
	public async Task ModsNotAllowed_Refused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Mods: GameMods.Key1));

		Assert.Equal(RoomOutcome.InvalidMods, result.Outcome);
		Assert.Equal(GameMods.NoMod, setup.Room.Settings.MatchSettings.Mods);
	}

	[Fact]
	public async Task ConfigureRoundRunning_Refused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Beatmap: RoomFixture.Beatmap));
		await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync();

		var result = await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange("renamed"));

		Assert.Equal(RoomOutcome.InProgress, result.Outcome);
		Assert.NotEqual("renamed", setup.Room.Settings.Name);
	}

	[Fact]
	public async Task ConfigureNothing_NoChange()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var events = app.RoomEvents.Events.Count;

		var result = await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(new RoomSettingsChange());

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Equal(events, app.RoomEvents.Events.Count);
	}
}