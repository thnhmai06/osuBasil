using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Domain.Multiplayer;
using Basil.Infrastructure.Chat.Handlers;
using Basil.Testing;

namespace Basil.Application.Multiplayer.Tests.Capabilities.Rooms;

/// <summary>
///     The room itself: the roles a caller holds in it and what closing it does. The expected behaviour comes from
///     AGENTS.md ("Authority") and docs/for-developers/multiplayer.md ("Creator, referee and host").
/// </summary>
public sealed class ScopedRoomTests
{
	[Fact]
	public async Task Close_ClearsEverything()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var channel = app.RoomChannels.Of(setup.Room);
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Beatmap: RoomFixture.Beatmap));
		await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync();

		var result = await setup.Room.ScopeTo(setup.Creator).CloseAsync();

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.True(setup.Room.IsClosed);
		Assert.Null(setup.Room.Slots.Find(setup.Player));
		Assert.True(setup.Room.Rounds.LastRound!.Aborted);
		Assert.DoesNotContain(setup.Room, app.Lobby.Rooms.Values);
		var closed = Assert.IsType<LobbyRoomClosed>(app.LobbyEvents.Events[^1]);
		Assert.Equal(3, closed.Evicted.Count);
		await new LobbyRoomClosedHandler(app.RoomChannels).Handle(closed, TestContext.Current.CancellationToken);
		Assert.True(channel.IsClosed);
	}

	[Fact]
	public async Task ClosedRoomClose_DoesNothing()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		Assert.True((await setup.Room.ScopeTo(setup.Creator).CloseAsync()).IsOk);
		var events = app.LobbyEvents.Events.Count;

		var result = await setup.Room.ScopeTo(setup.Creator).CloseAsync();

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Equal(events, app.LobbyEvents.Events.Count);
	}

	[Fact]
	public async Task JoinClosedRoom_RoomClosed()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		Assert.True((await setup.Room.ScopeTo(setup.Creator).CloseAsync()).IsOk);
		var player = await RoomFixture.PlayerAsync(app, "Latecomer");

		var result = await setup.Room.ScopeTo(player).Members.JoinAsync("");

		Assert.Equal(RoomOutcome.RoomClosed, result.Outcome);
	}

	[Fact]
	public async Task LeaveClosedRoom_RoomClosed()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		Assert.True((await setup.Room.ScopeTo(setup.Creator).CloseAsync()).IsOk);

		var result = await setup.Room.ScopeTo(setup.Player).Members.LeaveAsync();

		Assert.Equal(RoomOutcome.RoomClosed, result.Outcome);
	}

	[Fact]
	public async Task Creator_GetsCreatorRole()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var roles = setup.Room.ScopeTo(setup.Creator).Roles;

		Assert.True(roles.HasFlag(RoomRoles.Creator));
	}

	[Fact]
	public async Task Referee_GetsRefereeRole()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var roles = setup.Room.ScopeTo(setup.Referee).Roles;

		Assert.True(roles.HasFlag(RoomRoles.Referee));
		Assert.False(roles.HasFlag(RoomRoles.Creator));
	}

	[Fact]
	public async Task Host_GetsHostRole()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var roles = setup.Room.ScopeTo(setup.Host).Roles;

		Assert.True(roles.HasFlag(RoomRoles.Host));
		Assert.True(roles.HasFlag(RoomRoles.Player));
		Assert.False(roles.HasFlag(RoomRoles.Referee));
	}

	[Fact]
	public async Task SeatedPlayer_GetsPlayerRole()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var roles = setup.Room.ScopeTo(setup.Player).Roles;

		Assert.Equal(RoomRoles.Player, roles);
	}

	[Fact]
	public async Task Outsider_GetsNoRights()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var scope = setup.Room.ScopeTo(setup.Outsider);

		Assert.Equal(RoomRoles.None, scope.Roles);
		Assert.Equal(RoomPermissions.None, scope.Rights);
	}

	[Fact]
	public async Task SuspendedCreator_GetsNoRight()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var scope = setup.SuspendedRoom.ScopeTo(setup.SuspendedCreator);

		Assert.True(scope.Roles.HasFlag(RoomRoles.Creator));
		Assert.Equal(RoomPermissions.None, scope.Rights);
	}

	[Fact]
	public async Task EveryRoomManager_GetsEveryRight()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var scope = setup.Room.ScopeTo(setup.Manager);

		Assert.Equal(RoomPermissionsGroups.All, scope.Rights);
	}
}