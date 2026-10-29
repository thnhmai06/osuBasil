using Basil.Application.Users.Connections;
using Basil.Application.Multiplayer.Runtime.Events;
using Basil.Domain.Auth;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Multiplayer.Tests.Runtime;

/// <summary>
///     The lobby's own work, which the server calls without a caller: releasing a connection, removing a user
///     whose right to join rooms is suspended, and closing the matches that were left running.
/// </summary>
public sealed class LobbyTests
{
	[Fact]
	public async Task ReleaseAsync_ForAPlayerSeatedInARoom_RemovesThemFromTheRoom()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		await app.Lobby.ReleaseAsync(setup.Player, TestContext.Current.CancellationToken);

		Assert.Null(setup.Room.Slots.Find(setup.Player));
		Assert.IsType<RoomPlayerLeft>(app.RoomEvents.Events[^1]);
	}

	[Fact]
	public async Task ReleaseAsync_ForATourneyClientObservingARoom_RemovesThemFromTheRoom()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var observerUser = await app.CreateUserAsync("Observer", "password",
			UserPermissionsGroups.Default | UserPermissions.RoomObserve);
		var observer = (TourneyConnection)await app.LoginAsync(observerUser, ConnectionType.Tourney);
		Assert.True((await setup.Room.ScopeTo(observer).Members.ObserverJoinAsync(
			TestContext.Current.CancellationToken)).IsOk);

		await app.Lobby.ReleaseAsync(observer, TestContext.Current.CancellationToken);

		Assert.DoesNotContain(observer, setup.Room.Members.Observers);
		Assert.IsType<RoomObserverLeft>(app.RoomEvents.Events[^1]);
	}

	[Fact]
	public async Task RemoveSuspendedAsync_ForAUserWhoMayNotJoinRooms_RemovesThemFromTheirRoom()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await RestrictRoomJoinAsync(app, setup.Player.Record);

		await app.Lobby.RemoveSuspendedAsync(app.Users.Of(setup.Player.Record),
			TestContext.Current.CancellationToken);

		Assert.Null(setup.Room.Slots.Find(setup.Player));
	}

	[Fact]
	public async Task RemoveSuspendedAsync_ForAUserWhoMayNotJoinRooms_RemovesTheirRefereeEntries()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await RestrictRoomJoinAsync(app, setup.Referee.Record);

		await app.Lobby.RemoveSuspendedAsync(app.Users.Of(setup.Referee.Record),
			TestContext.Current.CancellationToken);

		Assert.DoesNotContain(setup.Referee.Record, setup.Room.Authority.Referees);
		Assert.IsType<RoomRefereeRemoved>(app.RoomEvents.Events[^1]);
	}

	[Fact]
	public async Task RemoveSuspendedAsync_ForAUserWhoMayStillJoinRooms_LeavesThemInTheirRoom()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		await app.Lobby.RemoveSuspendedAsync(app.Users.Of(setup.Player.Record),
			TestContext.Current.CancellationToken);

		Assert.NotNull(setup.Room.Slots.Find(setup.Player));
		Assert.Contains(setup.Referee.Record, setup.Room.Authority.Referees);
	}

	[Fact]
	public async Task CloseUnfinishedAsync_ForARunningMatch_EndsIt()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");
		var (room, opened) = await app.Lobby.ScopeTo(creator).OpenRoomAsync("test room", "", false, false,
			cancellationToken: TestContext.Current.CancellationToken);
		Assert.True(opened.IsOk);

		var ended = await app.Lobby.CloseUnfinishedAsync(TestContext.Current.CancellationToken);

		Assert.Equal(1, ended);
		Assert.NotNull(room!.Match.Value.EndedAt);
		Assert.Equal(room.Match, app.LobbyEvents.Events.OfType<LobbyMatchRecovered>().Single().Match);
	}

	[Fact]
	public async Task CloseAll_EndsTheMatchOfEveryOpenRoom_AndRemovesThemFromTheLobby()
	{
		using var app = new TestApplication();
		var first = await RoomFixture.CreateAsync(app);
		var creator = await RoomFixture.PlayerAsync(app, "SecondCreator");
		var (second, opened) = await app.Lobby.ScopeTo(creator).OpenRoomAsync("second room", "", false, false,
			cancellationToken: TestContext.Current.CancellationToken);
		Assert.True(opened.IsOk);

		var open = app.Lobby.Rooms.Count;

		var closed = app.Lobby.CloseAll();

		Assert.True(open >= 2);
		Assert.Equal(open, closed);
		Assert.NotNull(first.Room.Match.Value.EndedAt);
		Assert.NotNull(second!.Match.Value.EndedAt);
		Assert.Empty(app.Lobby.Rooms);
		Assert.Equal(open, app.LobbyEvents.Events.OfType<LobbyRoomClosed>().Count());
	}

	[Fact]
	public void CloseAll_WithoutAnyRoom_ClosesNothing()
	{
		using var app = new TestApplication();

		Assert.Equal(0, app.Lobby.CloseAll());
	}

	[Fact]
	public async Task CloseAll_CalledTwice_ClosesEachRoomOnce()
	{
		using var app = new TestApplication();
		await RoomFixture.CreateAsync(app);
		var open = app.Lobby.Rooms.Count;

		app.Lobby.CloseAll();
		Assert.Equal(0, app.Lobby.CloseAll());

		Assert.Equal(open, app.LobbyEvents.Events.OfType<LobbyRoomClosed>().Count());
	}

	[Fact]
	public async Task CloseUnfinishedAsync_WithoutAnyMatch_EndsNothing()
	{
		using var app = new TestApplication();

		var ended = await app.Lobby.CloseUnfinishedAsync(TestContext.Current.CancellationToken);

		Assert.Equal(0, ended);
	}

	[Fact]
	public void Find_AnIdNoRoomHas_ReturnsNothing()
	{
		using var app = new TestApplication();

		Assert.Null(app.Lobby.Find(99));
	}

	[Fact]
	public async Task Find_AnOpenRoom_ReturnsIt()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		Assert.Same(setup.Room, app.Lobby.Find(setup.Room.Id));
	}

	[Fact]
	public async Task Find_APrivateRoom_ReturnsItLikeAnyOtherBecausePrivateOnlyKeepsARoomOutOfListings()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");
		var (room, result) = await app.Lobby.ScopeTo(creator).OpenRoomAsync("private room", "", false, true,
			cancellationToken: TestContext.Current.CancellationToken);
		Assert.True(result.IsOk);

		Assert.Same(room, app.Lobby.Find(room!.Id));
	}

	[Fact]
	public async Task Find_ARoomThatClosed_ReturnsNothing()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		Assert.True((await setup.Room.ScopeTo(setup.Creator).CloseAsync(TestContext.Current.CancellationToken)).IsOk);

		Assert.Null(app.Lobby.Find(setup.Room.Id));
	}

	private static async Task RestrictRoomJoinAsync(TestApplication app, UserRecord target)
	{
		var moderator = await app.CreateUserAsync("Moderator", "password",
			UserPermissionsGroups.Default | UserPermissions.UserManageRestrict);
		var by = await app.LoginAsync(moderator);
		var (result, _) = await app.Users.Of(target).ScopeTo(by).Restrictions.RestrictAsync(
			UserPermissions.RoomJoin, null, cancellationToken: TestContext.Current.CancellationToken);
		Assert.True(result.IsOk);
	}
}