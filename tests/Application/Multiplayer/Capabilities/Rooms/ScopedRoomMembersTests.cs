using Basil.Application.Chat.Handlers;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Persistence;
using Basil.Domain.Auth;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Multiplayer.Tests.Capabilities.Rooms;

/// <summary>
///     Who belongs to a room and how they come and go. The expected behaviour comes from
///     docs/for-developers/multiplayer.md ("Membership, slots and settings") and the osu! multiplayer rules: a join
///     checks the password, the bans, the seat and the room's other members, and the host passes on when the host
///     leaves.
/// </summary>
public sealed class ScopedRoomMembersTests
{
	[Fact]
	public async Task JoinAsync_WithTheWrongPassword_IsRefusedAndTakesNoSlot()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Password: "secret"), TestContext.Current.CancellationToken);
		var player = await RoomFixture.PlayerAsync(app, "Latecomer");

		var result = await setup.Room.ScopeTo(player).Members.JoinAsync("wrong", TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.WrongPassword, result.Outcome);
		Assert.Null(setup.Room.Slots.Find(player));
	}

	[Fact]
	public async Task JoinAsync_WithTheRightPassword_SeatsThePlayer()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Password: "secret"), TestContext.Current.CancellationToken);
		var player = await RoomFixture.PlayerAsync(app, "Latecomer");

		var result = await setup.Room.ScopeTo(player).Members
			.JoinAsync("secret", TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.NotNull(setup.Room.Slots.Find(player));
	}

	[Fact]
	public async Task JoinAsync_WithoutAPassword_SeatsThePlayerInTheFirstOpenSlot()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var player = await RoomFixture.PlayerAsync(app, "Latecomer");

		var result = await setup.Room.ScopeTo(player).Members.JoinAsync("", TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Equal(4, setup.Room.Slots.Find(player)!.Index);
	}

	[Fact]
	public async Task JoinAsync_ToARoomWhoseSlotsAreLocked_SeatsThePlayer()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).Settings.SetLockedAsync(true, TestContext.Current.CancellationToken);
		var player = await RoomFixture.PlayerAsync(app, "Latecomer");

		var result = await setup.Room.ScopeTo(player).Members.JoinAsync("", TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.NotNull(setup.Room.Slots.Find(player));
	}

	[Fact]
	public async Task JoinAsync_ForABannedUser_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var player = await RoomFixture.PlayerAsync(app, "Banned");
		Assert.True((await setup.Room.ScopeTo(setup.Creator).Members.BanAsync(player.Record,
			TestContext.Current.CancellationToken)).IsOk);

		var result = await setup.Room.ScopeTo(player).Members.JoinAsync("", TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Banned, result.Outcome);
		Assert.Null(setup.Room.Slots.Find(player));
	}

	[Fact]
	public async Task JoinAsync_ForAFullRoom_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Size: 1), TestContext.Current.CancellationToken);
		var player = await RoomFixture.PlayerAsync(app, "Latecomer");

		var result = await setup.Room.ScopeTo(player).Members.JoinAsync("", TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Full, result.Outcome);
		Assert.Null(setup.Room.Slots.Find(player));
	}

	[Fact]
	public async Task JoinAsync_ForAPlayerWhoIsAlreadySeated_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Player).Members.JoinAsync("",
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.AlreadySeated, result.Outcome);
	}

	[Fact]
	public async Task JoinAsync_ForAPlayerWhoIsSeatedInAnotherRoom_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var other = await RoomFixture.PlayerAsync(app, "Other");
		var (otherRoom, opened) = await app.Lobby.ScopeTo(setup.Creator)
			.OpenRoomAsync("other room", "", true, false, cancellationToken: TestContext.Current.CancellationToken);
		Assert.True(opened.IsOk);
		Assert.True((await otherRoom!.ScopeTo(other).Members.JoinAsync("",
			TestContext.Current.CancellationToken)).IsOk);

		var result = await setup.Room.ScopeTo(other).Members.JoinAsync("", TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.InAnotherRoom, result.Outcome);
		Assert.Null(setup.Room.Slots.Find(other));
	}

	[Fact]
	public async Task LeaveAsync_ForTheHost_GivesTheHostToTheNextSeatedPlayer()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");
		var (room, opened) = await app.Lobby.ScopeTo(creator).OpenRoomAsync("test room", "", false, false,
			cancellationToken: TestContext.Current.CancellationToken);
		Assert.True(opened.IsOk);
		var player = await RoomFixture.PlayerAsync(app, "Player");
		Assert.True((await room!.ScopeTo(player).Members.JoinAsync("", TestContext.Current.CancellationToken)).IsOk);
		Assert.Same(creator, room.Authority.Host);

		Assert.True((await room.ScopeTo(creator).Members.LeaveAsync(TestContext.Current.CancellationToken)).IsOk);

		Assert.Same(player, room.Authority.Host);
	}

	[Fact]
	public async Task LeaveAsync_ForTheLastPlayerOfANonTournamentRoom_ClosesTheRoomAndItsChannel()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");
		var (room, opened) = await app.Lobby.ScopeTo(creator).OpenRoomAsync("test room", "", false, false,
			cancellationToken: TestContext.Current.CancellationToken);
		Assert.True(opened.IsOk);
		var channel = app.RoomChannels.Of(room!);

		Assert.True((await room!.ScopeTo(creator).Members.LeaveAsync(TestContext.Current.CancellationToken)).IsOk);

		Assert.True(room.IsClosed);
		var closed = Assert.IsType<LobbyRoomClosed>(app.LobbyEvents.Events[^1]);
		await new LobbyRoomClosedHandler(app.RoomChannels).Handle(closed, TestContext.Current.CancellationToken);
		Assert.True(channel.IsClosed);
	}

	[Fact]
	public async Task LeaveAsync_ForTheLastPlayerOfATournamentRoom_KeepsTheRoomOpen()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");
		var (room, opened) = await app.Lobby.ScopeTo(creator).OpenRoomAsync("test room", "", true, false,
			cancellationToken: TestContext.Current.CancellationToken);
		Assert.True(opened.IsOk);

		Assert.True((await room!.ScopeTo(creator).Members.LeaveAsync(TestContext.Current.CancellationToken)).IsOk);

		Assert.False(room.IsClosed);
		Assert.False(app.RoomChannels.Of(room).IsClosed);
	}

	[Fact]
	public async Task KickAsync_ForAPlayer_RemovesThemAndAnnouncesItOnce()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Members.KickAsync(setup.Player.Record,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Null(setup.Room.Slots.Find(setup.Player));
		Assert.Single(app.RoomEvents.Events, roomEvent => roomEvent is RoomPlayerKicked);
	}

	[Fact]
	public async Task BanAsync_ForAPlayer_RemovesThemAndBlocksThemFromJoiningAgain()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Members.BanAsync(setup.Player.Record,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Contains(setup.Player.Record, setup.Room.Members.Banned);
		Assert.Null(setup.Room.Slots.Find(setup.Player));
		var again = await setup.Room.ScopeTo(setup.Player).Members.JoinAsync("",
			TestContext.Current.CancellationToken);
		Assert.Equal(RoomOutcome.Banned, again.Outcome);
	}

	[Fact]
	public async Task UnbanAsync_ForABannedUser_LetsThemJoinAgain()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		Assert.True((await setup.Room.ScopeTo(setup.Creator).Members.BanAsync(setup.Player.Record,
			TestContext.Current.CancellationToken)).IsOk);

		var result = await setup.Room.ScopeTo(setup.Creator).Members.UnbanAsync(setup.Player.Record,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.DoesNotContain(setup.Player.Record, setup.Room.Members.Banned);
		var join = await setup.Room.ScopeTo(setup.Player).Members.JoinAsync("",
			TestContext.Current.CancellationToken);
		Assert.Equal(RoomOutcome.Ok, join.Outcome);
	}

	[Fact]
	public async Task KickAsync_ForTheCreator_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Referee).Members.KickAsync(setup.Creator.Record,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.IsManager, result.Outcome);
		Assert.NotNull(setup.Room.Slots.Find(setup.Creator));
	}

	[Fact]
	public async Task BanAsync_ForTheCreator_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Referee).Members.BanAsync(setup.Creator.Record,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.IsManager, result.Outcome);
		Assert.DoesNotContain(setup.Creator.Record, setup.Room.Members.Banned);
	}

	[Fact]
	public async Task ObserverJoinAsync_ForATourneyClient_AddsThemAsObserver()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var observer = await ObserverAsync(app, "Observer");

		var result = await setup.Room.ScopeTo(observer).Members.ObserverJoinAsync(
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Contains(observer, setup.Room.Members.Observers);
		Assert.IsType<RoomObserverJoined>(app.RoomEvents.Events[^1]);
	}

	[Fact]
	public async Task ObserverJoinAsync_ForAUserWhoIsSeated_IsRefused()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");
		var (room, opened) = await app.Lobby.ScopeTo(creator).OpenRoomAsync("test room", "", false, false,
			cancellationToken: TestContext.Current.CancellationToken);
		Assert.True(opened.IsOk);
		var user = await app.CreateUserAsync("Watcher", "password",
			UserPermissionsGroups.Default | UserPermissions.RoomObserve);
		var player = await RoomFixture.PlayerAsync(app, user);
		Assert.True((await room!.ScopeTo(player).Members.JoinAsync("", TestContext.Current.CancellationToken)).IsOk);
		var observer = (TourneyConnection)await app.LoginAsync(user, SessionType.OsuTourney);

		var result = await room.ScopeTo(observer).Members.ObserverJoinAsync(TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.IsPlayer, result.Outcome);
		Assert.DoesNotContain(observer, room.Members.Observers);
	}

	[Fact]
	public async Task ObserverLeaveAsync_ForAnObserver_RemovesThem()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var observer = await ObserverAsync(app, "Observer");
		Assert.True((await setup.Room.ScopeTo(observer).Members.ObserverJoinAsync(
			TestContext.Current.CancellationToken)).IsOk);

		var result = await setup.Room.ScopeTo(observer).Members.ObserverLeaveAsync(
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.DoesNotContain(observer, setup.Room.Members.Observers);
		Assert.IsType<RoomObserverLeft>(app.RoomEvents.Events[^1]);
	}

	[Fact]
	public async Task ObserverLeaveAsync_ForSomebodyWhoDoesNotObserve_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var observer = await ObserverAsync(app, "Observer");

		var result = await setup.Room.ScopeTo(observer).Members.ObserverLeaveAsync(
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.NotObserver, result.Outcome);
	}

	[Fact]
	public async Task JoinAsync_ForAnObserver_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var user = await app.CreateUserAsync("Watcher", "password",
			UserPermissionsGroups.Default | UserPermissions.RoomObserve);
		var player = await RoomFixture.PlayerAsync(app, user);
		var observer = (TourneyConnection)await app.LoginAsync(user, SessionType.OsuTourney);
		Assert.True((await setup.Room.ScopeTo(observer).Members.ObserverJoinAsync(
			TestContext.Current.CancellationToken)).IsOk);

		var result = await setup.Room.ScopeTo(player).Members.JoinAsync("", TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.IsObserver, result.Outcome);
		Assert.Null(setup.Room.Slots.Find(player));
	}

	[Fact]
	public async Task LoginAsync_AsATourneyClientWithoutRoomObserve_IsRefused()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Watcher", "password");

		var result = await app.AttemptLoginAsync(user.Value.Name, "password", SessionType.OsuTourney);

		Assert.Null(result.Connection);
		Assert.Equal(LoginFailure.NoTourneyPermission, result.Failure);
	}

	[Fact]
	public async Task InviteAsync_ToAnOfflineUser_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var offline = await app.CreateUserAsync("Offline", "password");
		var target = app.Users.Of(offline);

		var result = await setup.Room.ScopeTo(setup.Player).Members.InviteAsync(target,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.TargetOffline, result.Outcome);
	}

	[Fact]
	public async Task InviteAsync_ToAUserWhoBlocksTheInviter_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var target = app.Users.Of(setup.Outsider.Record);
		Assert.True((await app.Users.Of(setup.Player.Record).ScopeTo(setup.Outsider).Relationships
			.BlockAsync(TestContext.Current.CancellationToken)).IsOk);

		var result = await setup.Room.ScopeTo(setup.Player).Members.InviteAsync(target,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Blocked, result.Outcome);
	}

	[Fact]
	public async Task InviteAsync_FromAUserWhoManagesEveryRoom_ReachesAUserWhoBlockedThem()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var target = app.Users.Of(setup.Outsider.Record);
		Assert.True((await app.Users.Of(setup.Manager.Record).ScopeTo(setup.Outsider).Relationships
			.BlockAsync(TestContext.Current.CancellationToken)).IsOk);

		var result = await setup.Room.ScopeTo(setup.Manager).Members.InviteAsync(target,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.IsType<RoomPlayerInvited>(app.RoomEvents.Events[^1]);
	}

	[Fact]
	public async Task KickAsync_ForSomebodyWhoIsNotSeated_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Members.KickAsync(setup.Outsider.Record,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.NotInRoom, result.Outcome);
	}

	[Fact]
	public async Task UnbanAsync_ForSomebodyWhoIsNotBanned_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Members.UnbanAsync(setup.Outsider.Record,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.NotBanned, result.Outcome);
	}

	[Fact]
	public async Task JoinAsync_ForAUserWhoMayNotJoinRooms_IsDeniedNamingRoomJoin()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.NoRoomJoin).Members.JoinAsync("",
			TestContext.Current.CancellationToken);

		Assert.False(result.IsOk);
		Assert.Equal(UserPermissions.RoomJoin, result.Missing);
		Assert.Null(setup.Room.Slots.Find(setup.NoRoomJoin));
	}

	[Fact]
	public async Task SeatAsync_ForAPlayerWhoMayNotJoinRooms_IsDeniedNamingRoomJoin()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Members.SeatAsync(setup.NoRoomJoin,
			TestContext.Current.CancellationToken);

		Assert.False(result.IsOk);
		Assert.Equal(UserPermissions.RoomJoin, result.Missing);
		Assert.Null(setup.Room.Slots.Find(setup.NoRoomJoin));
	}

	[Fact]
	public async Task JoinAsync_ForANewConnectionOfTheSameUser_TakesOverTheSeatOfTheClosedOne()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		app.Clock.Advance(TimeSpan.FromSeconds(2));
		app.Users.Close(setup.Player, SessionEndReason.LoggedOut);
		var again = await RoomFixture.PlayerAsync(app, setup.Player.Record);

		var result = await setup.Room.ScopeTo(again).Members.JoinAsync("", TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Same(again, setup.Room.Slots.Find(setup.Player.Record)!.Player);
		var joined = Assert.IsType<RoomPlayerJoined>(app.RoomEvents.Events[^1]);
		Assert.Same(setup.Player, joined.Replaced);
	}

	private static async Task<TourneyConnection> ObserverAsync(TestApplication app, string name)
	{
		var user = await app.CreateUserAsync(name, "password",
			UserPermissionsGroups.Default | UserPermissions.RoomObserve);
		return (TourneyConnection)await app.LoginAsync(user, SessionType.OsuTourney);
	}
}