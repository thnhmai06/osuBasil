using Basil.Application.Runtime.Multiplayer;
using Basil.Application.Runtime.Multiplayer.Events;
using Basil.Testing;
using Basil.Domain.Multiplayer;

namespace Basil.Application.Runtime.Tests.Multiplayer.Rooms;

/// <summary>
///     The room itself: the roles a caller holds in it and what closing it does. The expected behaviour comes from
///     AGENTS.md ("Authority") and docs/for-developers/multiplayer.md ("Creator, referee and host").
/// </summary>
public sealed class ScopedRoomTests
{
	[Fact]
	public async Task CloseAsync_EvictsThePlayersEndsTheRoundClosesTheChannelAndForgetsTheRoom()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Beatmap: RoomFixture.Beatmap), TestContext.Current.CancellationToken);
		await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync(TestContext.Current.CancellationToken);

		var result = await setup.Room.ScopeTo(setup.Creator).CloseAsync(TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.True(setup.Room.IsClosed);
		Assert.Null(setup.Room.Slots.Find(setup.Player));
		Assert.True(setup.Room.Rounds.LastRound!.Aborted);
		Assert.True(setup.Room.Channel.IsClosed);
		Assert.DoesNotContain(setup.Room, app.Lobby.Rooms.Values);
		var closed = Assert.IsType<LobbyRoomClosed>(app.LobbyEvents.Events[^1]);
		Assert.Equal(3, closed.Evicted.Count);
	}

	[Fact]
	public async Task CloseAsync_ForAClosedRoom_DoesNothing()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		Assert.True((await setup.Room.ScopeTo(setup.Creator).CloseAsync(
			TestContext.Current.CancellationToken)).IsOk);
		var events = app.LobbyEvents.Events.Count;

		var result = await setup.Room.ScopeTo(setup.Creator).CloseAsync(TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Equal(events, app.LobbyEvents.Events.Count);
	}

	[Fact]
	public async Task JoinAsync_ForAClosedRoom_ReturnsRoomClosed()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		Assert.True((await setup.Room.ScopeTo(setup.Creator).CloseAsync(
			TestContext.Current.CancellationToken)).IsOk);
		var player = await RoomFixture.PlayerAsync(app, "Latecomer");

		var result = await setup.Room.ScopeTo(player).Members.JoinAsync("", TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.RoomClosed, result.Outcome);
	}

	[Fact]
	public async Task LeaveAsync_ForAClosedRoom_ReturnsRoomClosed()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		Assert.True((await setup.Room.ScopeTo(setup.Creator).CloseAsync(
			TestContext.Current.CancellationToken)).IsOk);

		var result = await setup.Room.ScopeTo(setup.Player).Members.LeaveAsync(
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.RoomClosed, result.Outcome);
	}

	[Fact]
	public async Task ScopeTo_ForTheCreator_GivesThemTheCreatorRole()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var roles = setup.Room.ScopeTo(setup.Creator).Roles;

		Assert.True(roles.HasFlag(RoomRoles.Creator));
	}

	[Fact]
	public async Task ScopeTo_ForAReferee_GivesThemTheRefereeRole()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var roles = setup.Room.ScopeTo(setup.Referee).Roles;

		Assert.True(roles.HasFlag(RoomRoles.Referee));
		Assert.False(roles.HasFlag(RoomRoles.Creator));
	}

	[Fact]
	public async Task ScopeTo_ForTheHost_GivesThemTheHostRoleButNoRefereeRole()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var roles = setup.Room.ScopeTo(setup.Host).Roles;

		Assert.True(roles.HasFlag(RoomRoles.Host));
		Assert.True(roles.HasFlag(RoomRoles.Player));
		Assert.False(roles.HasFlag(RoomRoles.Referee));
	}

	[Fact]
	public async Task ScopeTo_ForASeatedPlayer_GivesThemThePlayerRole()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var roles = setup.Room.ScopeTo(setup.Player).Roles;

		Assert.Equal(RoomRoles.Player, roles);
	}

	[Fact]
	public async Task ScopeTo_ForAnOutsider_GivesThemNoRoleAndNoRight()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var scope = setup.Room.ScopeTo(setup.Outsider);

		Assert.Equal(RoomRoles.None, scope.Roles);
		Assert.Equal(RoomPermissions.None, scope.Rights);
	}

	[Fact]
	public async Task ScopeTo_ForACreatorWhoseRoomJoinIsSuspended_GivesThemNoRight()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var scope = setup.SuspendedRoom.ScopeTo(setup.SuspendedCreator);

		Assert.True(scope.Roles.HasFlag(RoomRoles.Creator));
		Assert.Equal(RoomPermissions.None, scope.Rights);
	}

	[Fact]
	public async Task ScopeTo_ForAUserWhoManagesEveryRoom_GivesThemEveryRight()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var scope = setup.Room.ScopeTo(setup.Manager);

		Assert.Equal(RoomPermissionsGroups.All, scope.Rights);
	}
}