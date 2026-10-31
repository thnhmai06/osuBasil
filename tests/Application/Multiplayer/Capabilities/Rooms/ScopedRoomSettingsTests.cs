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
			new RoomSettingsChange("renamed"), TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Equal("renamed", setup.Room.Settings.Name);
		var changed = Assert.Single(app.RoomEvents.Events.OfType<RoomSettingsChanged>());
		Assert.Equal("renamed", changed.Change.Name);
	}

	[Fact]
	public async Task ConfigureAsync_ChangesThePasswordAndKeepsItOutOfTheEvent()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Password: "secret"), TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Equal("secret", setup.Room.Settings.Password);
		var changed = Assert.Single(app.RoomEvents.Events.OfType<RoomSettingsChanged>());
		Assert.True(changed.PasswordChanged);
		Assert.Null(changed.Change.Password);
	}

	[Fact]
	public async Task ConfigureAsync_WithAnEmptyPassword_RemovesThePassword()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Password: "secret"), TestContext.Current.CancellationToken);

		var result = await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Password: ""), TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Equal("", setup.Room.Settings.Password);
	}

	[Fact]
	public async Task ConfigureAsync_ChangesTheMode()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Mode: GameMode.Mania), TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Equal(GameMode.Mania, setup.Room.Settings.MatchSettings.Mode);
	}

	[Fact]
	public async Task ConfigureAsync_ChangingTheMode_DropsTheModsTheModeDoesNotAllow()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Mods: GameMods.Autopilot), TestContext.Current.CancellationToken);
		Assert.Equal(GameMods.Autopilot, setup.Room.Settings.MatchSettings.Mods);

		var result = await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Mode: GameMode.Mania), TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Equal(GameMods.NoMod, setup.Room.Settings.MatchSettings.Mods);
	}

	[Fact]
	public async Task ConfigureAsync_ChangesTheWinCondition()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(WinCondition: GameWinCondition.Accuracy), TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Equal(GameWinCondition.Accuracy, setup.Room.Settings.MatchSettings.WinCondition);
	}

	[Fact]
	public async Task ConfigureAsync_ChangingTheTeamType_ReassignsTheTeams()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(TeamType: GameTeamType.TeamVs), TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Equal(GameTeamType.TeamVs, setup.Room.Settings.MatchSettings.TeamType);
		Assert.Equal(GameTeam.Red, setup.Room.Slots.Find(setup.Creator)!.Team);
		Assert.Equal(GameTeam.Blue, setup.Room.Slots.Find(setup.Host)!.Team);
	}

	[Fact]
	public async Task ConfigureAsync_ChangesTheBeatmapAndSetsTheReadyPlayersBackToNotReady()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		Assert.True((await setup.Room.ScopeTo(setup.Player).Slots.SetReadyAsync(true,
			TestContext.Current.CancellationToken)).IsOk);

		var result = await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Beatmap: RoomFixture.Beatmap), TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Equal(RoomFixture.Beatmap, setup.Room.Settings.Beatmap);
		Assert.Equal(RoomSlotStatus.NotReady, setup.Room.Slots.Find(setup.Player)!.Status);
	}

	[Fact]
	public async Task ConfigureAsync_TurningFreemodOn_GivesTheRoomsModsToThePlayers()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Mods: GameMods.Hidden), TestContext.Current.CancellationToken);

		var result = await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Freemods: true), TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Equal(GameMods.NoMod, setup.Room.Settings.MatchSettings.Mods);
		Assert.Equal(GameMods.Hidden, setup.Room.Slots.Find(setup.Player)!.Mods);
	}

	[Fact]
	public async Task ConfigureAsync_TurningFreemodOff_GivesTheRoomTheHostsMods()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Mods: GameMods.Hidden), TestContext.Current.CancellationToken);
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Freemods: true), TestContext.Current.CancellationToken);

		var result = await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Freemods: false), TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Equal(GameMods.Hidden, setup.Room.Settings.MatchSettings.Mods);
		Assert.Equal(GameMods.NoMod, setup.Room.Slots.Find(setup.Player)!.Mods);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("bad\0name")]
	public async Task ConfigureAsync_WithAnInvalidName_IsRefused(string name)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(name), TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.InvalidSettings, result.Outcome);
		Assert.NotEqual(name, setup.Room.Settings.Name);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(17)]
	public async Task ConfigureAsync_WithAnInvalidSize_IsRefused(int size)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Size: size), TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.InvalidSettings, result.Outcome);
	}

	[Fact]
	public async Task ConfigureAsync_WithModsTheModeDoesNotAllow_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Mods: GameMods.Key1), TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.InvalidMods, result.Outcome);
		Assert.Equal(GameMods.NoMod, setup.Room.Settings.MatchSettings.Mods);
	}

	[Fact]
	public async Task ConfigureAsync_WhileARoundIsInProgress_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Beatmap: RoomFixture.Beatmap), TestContext.Current.CancellationToken);
		await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync(TestContext.Current.CancellationToken);

		var result = await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange("renamed"), TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.InProgress, result.Outcome);
		Assert.NotEqual("renamed", setup.Room.Settings.Name);
	}

	[Fact]
	public async Task ConfigureAsync_WithNothingToChange_ChangesNothing()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var events = app.RoomEvents.Events.Count;

		var result = await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(new RoomSettingsChange(),
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Equal(events, app.RoomEvents.Events.Count);
	}
}