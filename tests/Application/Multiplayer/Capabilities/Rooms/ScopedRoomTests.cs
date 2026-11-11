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
	public async Task Creator_GetsCreatorPrivileges()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var privileges = setup.Room.ScopeTo(setup.Creator).Privileges;

		Assert.True(privileges.HasFlag(RoomPrivileges.Creator));
	}

	[Fact]
	public async Task Referee_GetsRefereePrivileges()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var privileges = setup.Room.ScopeTo(setup.Referee).Privileges;

		Assert.True(privileges.HasFlag(RoomPrivileges.Referee));
		Assert.False(privileges.HasFlag(RoomPrivileges.Creator));
	}

	[Fact]
	public async Task Host_GetsHostPrivileges()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var privileges = setup.Room.ScopeTo(setup.Host).Privileges;

		Assert.True(privileges.HasFlag(RoomPrivileges.Host));
		Assert.True(privileges.HasFlag(RoomPrivileges.Player));
		Assert.False(privileges.HasFlag(RoomPrivileges.Referee));
	}

	[Fact]
	public async Task SeatedPlayer_GetsPlayerPrivileges()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var privileges = setup.Room.ScopeTo(setup.Player).Privileges;

		Assert.Equal(RoomPrivileges.Player, privileges);
	}

	[Fact]
	public async Task Outsider_GetsNoPrivileges()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var scope = setup.Room.ScopeTo(setup.Outsider);

		Assert.Equal(RoomPrivileges.None, scope.Privileges);
	}

	[Fact]
	public async Task SuspendedCreator_GetsNoPrivilege()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var scope = setup.SuspendedRoom.ScopeTo(setup.SuspendedCreator);

		Assert.Equal(RoomPrivileges.None, scope.Privileges);
	}

	[Fact]
	public async Task EveryRoomManager_GetsEveryPrivilege()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var scope = setup.Room.ScopeTo(setup.Manager);

		Assert.Equal(RoomPrivileges.Creator, scope.Privileges);
	}
}