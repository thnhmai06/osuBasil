using Basil.Application.Multiplayer.Objects;
using Basil.Application.Users.Objects;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Persistence;
using Basil.Domain.Authentication;
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
	public async Task WrongPassword_RefusedNoSlot()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Password: "secret"));
		var player = await RoomFixture.PlayerAsync(app, "Latecomer");

		var result = await setup.Room.ScopeTo(player).Members.JoinAsync("wrong");

		Assert.Equal(RoomOutcome.WrongPassword, result);
		Assert.Null(setup.Room.Slots.Find(player));
	}

	[Fact]
	public async Task RightPassword_SeatsPlayer()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Password: "secret"));
		var player = await RoomFixture.PlayerAsync(app, "Latecomer");

		var result = await setup.Room.ScopeTo(player).Members
			.JoinAsync("secret");

		Assert.Equal(RoomOutcome.Ok, result);
		Assert.NotNull(setup.Room.Slots.Find(player));
	}

	[Fact]
	public async Task NoPassword_SeatsFirstOpen()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var player = await RoomFixture.PlayerAsync(app, "Latecomer");

		var result = await setup.Room.ScopeTo(player).Members.JoinAsync("");

		Assert.Equal(RoomOutcome.Ok, result);
		Assert.Equal(4, setup.Room.Slots.Find(player)!.Index);
	}

	[Fact]
	public async Task LockedSlots_SeatsPlayer()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).Settings.SetLockedAsync(true);
		var player = await RoomFixture.PlayerAsync(app, "Latecomer");

		var result = await setup.Room.ScopeTo(player).Members.JoinAsync("");

		Assert.Equal(RoomOutcome.Ok, result);
		Assert.NotNull(setup.Room.Slots.Find(player));
	}

	[Fact]
	public async Task BannedUser_Refused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var player = await RoomFixture.PlayerAsync(app, "Banned");
		Assert.True((await setup.Room.ScopeTo(setup.Creator).Members.BanAsync(UserReference.ById(player.Record.Id))) == RoomOutcome.Ok);

		var result = await setup.Room.ScopeTo(player).Members.JoinAsync("");

		Assert.Equal(RoomOutcome.Banned, result);
		Assert.Null(setup.Room.Slots.Find(player));
	}

	[Fact]
	public async Task FullRoom_Refused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Size: 1));
		var player = await RoomFixture.PlayerAsync(app, "Latecomer");

		var result = await setup.Room.ScopeTo(player).Members.JoinAsync("");

		Assert.Equal(RoomOutcome.Full, result);
		Assert.Null(setup.Room.Slots.Find(player));
	}

	[Fact]
	public async Task AlreadySeated_Refused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Player).Members.JoinAsync("");

		Assert.Equal(RoomOutcome.AlreadySeated, result);
	}

	[Fact]
	public async Task SeatedElsewhere_Refused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var other = await RoomFixture.PlayerAsync(app, "Other");
		var (otherRoom, opened) = await app.Lobby.ScopeTo(setup.Creator)
			.OpenRoomAsync("other room", "", RoomSource.Api, false);
		Assert.True(opened == RoomOutcome.Ok);
		Assert.True((await otherRoom!.ScopeTo(other).Members.JoinAsync("")) == RoomOutcome.Ok);

		var result = await setup.Room.ScopeTo(other).Members.JoinAsync("");

		Assert.Equal(RoomOutcome.InAnotherRoom, result);
		Assert.Null(setup.Room.Slots.Find(other));
	}

	[Fact]
	public async Task HostLeaves_HostPasses()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");
		var (room, opened) = await app.Lobby.ScopeTo(creator).OpenRoomAsync("test room", "", RoomSource.Game, false);
		Assert.True(opened == RoomOutcome.Ok);
		var player = await RoomFixture.PlayerAsync(app, "Player");
		Assert.True((await room!.ScopeTo(player).Members.JoinAsync("")) == RoomOutcome.Ok);
		Assert.Same(creator, room.Authority.Host);

		Assert.True((await room.ScopeTo(creator).Members.LeaveAsync()) == RoomOutcome.Ok);

		Assert.Same(player, room.Authority.Host);
	}

	[Theory]
	[InlineData(RoomSource.Game)]
	[InlineData(RoomSource.Api)]
	public async Task LastPlayerLeaves_OpenAnnouncesClose(RoomSource source)
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");
		var (room, opened) = await app.Lobby.ScopeTo(creator).OpenRoomAsync("test room", "", source, false);
		Assert.True(opened == RoomOutcome.Ok);

		Assert.True((await room!.ScopeTo(creator).Members.LeaveAsync()) == RoomOutcome.Ok);

		Assert.False(room.IsClosed);
		Assert.False(app.RoomChannels.Of(room).IsClosed);
		var announced = Assert.IsType<LobbyRoomClosingAnnounced>(app.LobbyEvents.Events[^1]);
		Assert.Equal(app.Clock.GetUtcNow() + Lobby.EmptyRoomTimeout, announced.ClosesAt);
	}

	[Fact]
	public async Task Player_RemovedAnnouncedOnce()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Members.KickAsync(UserReference.ById(setup.Player.Record.Id));

		Assert.Equal(RoomOutcome.Ok, result);
		Assert.Null(setup.Room.Slots.Find(setup.Player));
		Assert.Single(app.RoomEvents.Events, roomEvent => roomEvent is RoomPlayerKicked);
	}

	[Fact]
	public async Task Player_RemovedAndBlocked()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Members.BanAsync(UserReference.ById(setup.Player.Record.Id));

		Assert.Equal(RoomOutcome.Ok, result);
		Assert.Contains(setup.Player.Record, setup.Room.Members.Banned);
		Assert.Null(setup.Room.Slots.Find(setup.Player));
		var again = await setup.Room.ScopeTo(setup.Player).Members.JoinAsync("");
		Assert.Equal(RoomOutcome.Banned, again);
	}

	[Fact]
	public async Task BannedUser_JoinsAgain()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		Assert.True((await setup.Room.ScopeTo(setup.Creator).Members.BanAsync(UserReference.ById(setup.Player.Record.Id))) == RoomOutcome.Ok);

		var result = await setup.Room.ScopeTo(setup.Creator).Members.UnbanAsync(UserReference.ById(setup.Player.Record.Id));

		Assert.Equal(RoomOutcome.Ok, result);
		Assert.DoesNotContain(setup.Player.Record, setup.Room.Members.Banned);
		var join = await setup.Room.ScopeTo(setup.Player).Members.JoinAsync("");
		Assert.Equal(RoomOutcome.Ok, join);
	}

	[Fact]
	public async Task KickCreator_Refused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Referee).Members.KickAsync(UserReference.ById(setup.Creator.Record.Id));

		Assert.Equal(RoomOutcome.IsManager, result);
		Assert.NotNull(setup.Room.Slots.Find(setup.Creator));
	}

	[Fact]
	public async Task BanCreator_Refused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Referee).Members.BanAsync(UserReference.ById(setup.Creator.Record.Id));

		Assert.Equal(RoomOutcome.IsManager, result);
		Assert.DoesNotContain(setup.Creator.Record, setup.Room.Members.Banned);
	}

	[Fact]
	public async Task TourneyClient_AddedAsObserver()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var observer = await ObserverAsync(app, "Observer");

		var result = await setup.Room.ScopeTo(observer).Members.ObserverJoinAsync();

		Assert.Equal(RoomOutcome.Ok, result);
		Assert.Contains(observer, setup.Room.Members.Observers);
		Assert.IsType<RoomObserverJoined>(app.RoomEvents.Events[^1]);
	}

	[Fact]
	public async Task SeatedUser_Refused()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");
		var (room, opened) = await app.Lobby.ScopeTo(creator).OpenRoomAsync("test room", "", RoomSource.Game, false);
		Assert.True(opened == RoomOutcome.Ok);
		var user = await app.CreateUserAsync("Watcher", "password",
			UserPrivilegesExtensions.Default | UserPrivileges.Tournament);
		var player = await RoomFixture.PlayerAsync(app, user);
		Assert.True((await room!.ScopeTo(player).Members.JoinAsync("")) == RoomOutcome.Ok);
		var observer = (TourneyConnection)await app.LoginAsync(user, SessionType.StableTourney);

		var result = await room.ScopeTo(observer).Members.ObserverJoinAsync();

		Assert.Equal(RoomOutcome.IsPlayer, result);
		Assert.DoesNotContain(observer, room.Members.Observers);
	}

	[Fact]
	public async Task ForAnObserver_RemovesThem()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var observer = await ObserverAsync(app, "Observer");
		Assert.True((await setup.Room.ScopeTo(observer).Members.ObserverJoinAsync()) == RoomOutcome.Ok);

		var result = await setup.Room.ScopeTo(observer).Members.ObserverLeaveAsync();

		Assert.Equal(RoomOutcome.Ok, result);
		Assert.DoesNotContain(observer, setup.Room.Members.Observers);
		Assert.IsType<RoomObserverLeft>(app.RoomEvents.Events[^1]);
	}

	[Fact]
	public async Task NonObserver_Refused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var observer = await ObserverAsync(app, "Observer");

		var result = await setup.Room.ScopeTo(observer).Members.ObserverLeaveAsync();

		Assert.Equal(RoomOutcome.NotObserver, result);
	}

	[Fact]
	public async Task ForAnObserver_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var user = await app.CreateUserAsync("Watcher", "password",
			UserPrivilegesExtensions.Default | UserPrivileges.Tournament);
		var player = await RoomFixture.PlayerAsync(app, user);
		var observer = (TourneyConnection)await app.LoginAsync(user, SessionType.StableTourney);
		Assert.True((await setup.Room.ScopeTo(observer).Members.ObserverJoinAsync()) == RoomOutcome.Ok);

		var result = await setup.Room.ScopeTo(player).Members.JoinAsync("");

		Assert.Equal(RoomOutcome.IsObserver, result);
		Assert.Null(setup.Room.Slots.Find(player));
	}

	[Fact]
	public async Task TourneyNoObserve_Refused()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Watcher", "password");

		var result = await app.AttemptLoginAsync(user.Value.Name, "password", SessionType.StableTourney);

		Assert.Null(result.Connection);
		Assert.Equal(LoginFailure.NoTourneyPrivilege, result.Failure);
	}

	[Fact]
	public async Task OfflineUser_Refused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var offline = await app.CreateUserAsync("Offline", "password");
		var target = app.Users.Of(offline);

		var result = await setup.Room.ScopeTo(setup.Player).Members.InviteAsync(target);

		Assert.Equal(RoomOutcome.TargetOffline, result);
	}

	[Fact]
	public async Task BlockingUser_Refused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var target = app.Users.Of(setup.Outsider.Record);
		Assert.True((await app.Users.Of(setup.Player.Record).ScopeTo(setup.Outsider).Relationships
			.BlockAsync()) == UserOutcome.Ok);

		var result = await setup.Room.ScopeTo(setup.Player).Members.InviteAsync(target);

		Assert.Equal(RoomOutcome.Blocked, result);
	}

	[Fact]
	public async Task ManagerInvite_ReachesBlocker()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var target = app.Users.Of(setup.Outsider.Record);
		Assert.True((await app.Users.Of(setup.Manager.Record).ScopeTo(setup.Outsider).Relationships
			.BlockAsync()) == UserOutcome.Ok);

		var result = await setup.Room.ScopeTo(setup.Manager).Members.InviteAsync(target);

		Assert.Equal(RoomOutcome.Ok, result);
		Assert.IsType<RoomPlayerInvited>(app.RoomEvents.Events[^1]);
	}

	[Fact]
	public async Task KickNotSeated_Refused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Members.KickAsync(UserReference.ById(setup.Outsider.Record.Id));

		Assert.Equal(RoomOutcome.NotInRoom, result);
	}

	[Fact]
	public async Task NoAccountUser_NotInRoom()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Members.KickAsync(UserReference.ById(int.MaxValue));

		Assert.Equal(RoomOutcome.NotInRoom, result);
	}

	[Fact]
	public async Task NoRole_RefusedAlike()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var player = setup.Room.ScopeTo(setup.Player).Members;

		var known = await player.KickAsync(UserReference.ById(setup.Outsider.Record.Id));
		var unknown = await player.KickAsync(UserReference.ById(int.MaxValue));

		Assert.Equal(RoomOutcome.RoleDenied, known);
		Assert.Equal(known, unknown);
		Assert.Equal(known, unknown);
	}

	[Fact]
	public async Task NotBanned_Refused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Members.UnbanAsync(UserReference.ById(setup.Outsider.Record.Id));

		Assert.Equal(RoomOutcome.NotBanned, result);
	}

	[Fact]
	public async Task NoJoinPrivilege_DeniedNamingJoin()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.NoRoomJoin).Members.JoinAsync("");

		Assert.False(result == RoomOutcome.Ok);
		Assert.Equal(RoomOutcome.Denied, result);
		Assert.Null(setup.Room.Slots.Find(setup.NoRoomJoin));
	}

	[Fact]
	public async Task ClosedRoom_DeniedNamingJoin()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).CloseAsync();

		var result = await setup.Room.ScopeTo(setup.NoRoomJoin).Members.JoinAsync("");

		Assert.Equal(RoomOutcome.Denied, result);
	}

	[Fact]
	public async Task NewConnection_TakesOverSeat()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		app.Clock.Advance(TimeSpan.FromSeconds(2));
		app.Users.Close(setup.Player, SessionEndReason.LoggedOut);
		var again = await RoomFixture.PlayerAsync(app, setup.Player.Record);

		var result = await setup.Room.ScopeTo(again).Members.JoinAsync("");

		Assert.Equal(RoomOutcome.Ok, result);
		Assert.Same(again, setup.Room.Slots.Find(setup.Player.Record)!.Player);
		var joined = Assert.IsType<RoomPlayerJoined>(app.RoomEvents.Events[^1]);
		Assert.Same(setup.Player, joined.Replaced);
	}

	private static async Task<TourneyConnection> ObserverAsync(TestApplication app, string name)
	{
		var user = await app.CreateUserAsync(name, "password",
			UserPrivilegesExtensions.Default | UserPrivileges.Tournament);
		return (TourneyConnection)await app.LoginAsync(user, SessionType.StableTourney);
	}
}