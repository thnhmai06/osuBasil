using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Authentication;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Multiplayer.Tests.Registry;

/// <summary>
///     The lobby's own work, which the server calls without a caller: releasing a connection, removing a user
///     whose right to join rooms is suspended, and closing the matches that were left running.
/// </summary>
public sealed class LobbyTests
{
	private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);

	[Fact]
	public async Task ReleaseAsync_ForAPlayerSeatedInARoom_RemovesThemFromTheRoom()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		await app.Lobby.ReleaseAsync(setup.Player);

		Assert.Null(setup.Room.Slots.Find(setup.Player));
		Assert.IsType<RoomPlayerLeft>(app.RoomEvents.Events[^1]);
	}

	[Fact]
	public async Task ReleaseAsync_ForATourneyClientObservingARoom_RemovesThemFromTheRoom()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var observerUser = await app.CreateUserAsync("Observer", "password",
			UserPrivilegesExtensions.Default | UserPrivileges.Tournament);
		var observer = (TourneyConnection)await app.LoginAsync(observerUser, SessionType.StableTourney);
		Assert.True((await setup.Room.ScopeTo(observer).Members.ObserverJoinAsync()).IsOk);

		await app.Lobby.ReleaseAsync(observer);

		Assert.DoesNotContain(observer, setup.Room.Members.Observers);
		Assert.IsType<RoomObserverLeft>(app.RoomEvents.Events[^1]);
	}

	[Fact]
	public async Task CloseUnfinishedAsync_ForARunningMatch_EndsIt()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");
		var (room, opened) = await app.Lobby.ScopeTo(creator).OpenRoomAsync("test room", "", RoomSource.Game, false);
		Assert.True(opened.IsOk);

		var ended = await app.Lobby.CloseUnfinishedAsync();

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
		var (second, opened) = await app.Lobby.ScopeTo(creator).OpenRoomAsync("second room", "", RoomSource.Game, false);
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

		var ended = await app.Lobby.CloseUnfinishedAsync();

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
		var (room, result) = await app.Lobby.ScopeTo(creator).OpenRoomAsync("private room", "", RoomSource.Game, true);
		Assert.True(result.IsOk);

		Assert.Same(room, app.Lobby.Find(room!.Id));
	}

	[Fact]
	public async Task Find_ARoomThatClosed_ReturnsNothing()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		Assert.True((await setup.Room.ScopeTo(setup.Creator).CloseAsync()).IsOk);

		Assert.Null(app.Lobby.Find(setup.Room.Id));
	}

	[Theory]
	[InlineData(RoomSource.Game)]
	[InlineData(RoomSource.Api)]
	public async Task CloseEmptyRooms_ForARoomEmptyForFifteenMinutes_ClosesItOnlyThen(RoomSource source)
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");
		var (room, opened) = await app.Lobby.ScopeTo(creator).OpenRoomAsync("test room", "", source, false);
		Assert.True(opened.IsOk);
		Assert.True((await room!.ScopeTo(creator).Members.LeaveAsync()).IsOk);

		app.Clock.Advance(Lobby.EmptyRoomTimeout - OneSecond);
		var early = app.Lobby.CloseEmptyRooms();
		app.Clock.Advance(OneSecond);
		var closed = app.Lobby.CloseEmptyRooms();

		Assert.Equal(0, early);
		Assert.Equal(1, closed);
		Assert.True(room.IsClosed);
		Assert.Null(Assert.IsType<LobbyRoomClosed>(app.LobbyEvents.Events[^1]).Actor);
	}

	[Fact]
	public async Task CloseEmptyRooms_ForARoomOpenedWithNobodySeated_ClosesItAfterFifteenMinutes()
	{
		using var app = new TestApplication();
		var creator = await app.LoginAsync(await app.CreateUserAsync("Creator", "password"));
		var (room, opened) = await app.Lobby.ScopeTo(creator).OpenRoomAsync("test room", "", RoomSource.Api, false);
		Assert.True(opened.IsOk);

		app.Clock.Advance(Lobby.EmptyRoomTimeout);

		Assert.Equal(1, app.Lobby.CloseEmptyRooms());
		Assert.True(room!.IsClosed);
	}

	[Fact]
	public async Task CloseEmptyRooms_ForARoomWithAPlayer_LeavesItOpen()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");
		var (room, opened) = await app.Lobby.ScopeTo(creator).OpenRoomAsync("test room", "", RoomSource.Game, false);
		Assert.True(opened.IsOk);

		app.Clock.Advance(Lobby.EmptyRoomTimeout * 2);

		Assert.Equal(0, app.Lobby.CloseEmptyRooms());
		Assert.False(room!.IsClosed);
	}

	[Fact]
	public async Task CloseEmptyRooms_ForARoomThatWasOccupiedThenEmptiedAgain_StartsTheClockOver()
	{
		using var app = new TestApplication();
		var creator = await app.LoginAsync(await app.CreateUserAsync("Creator", "password"));
		var (room, opened) = await app.Lobby.ScopeTo(creator).OpenRoomAsync("test room", "", RoomSource.Api, false);
		Assert.True(opened.IsOk);
		app.Clock.Advance(TimeSpan.FromMinutes(10));
		var player = await RoomFixture.PlayerAsync(app, "Player");
		Assert.True((await room!.ScopeTo(player).Members.JoinAsync("")).IsOk);
		Assert.True((await room.ScopeTo(player).Members.LeaveAsync()).IsOk);

		app.Clock.Advance(Lobby.EmptyRoomTimeout - OneSecond);
		var early = app.Lobby.CloseEmptyRooms();
		app.Clock.Advance(OneSecond);
		var closed = app.Lobby.CloseEmptyRooms();

		Assert.Equal(0, early);
		Assert.Equal(1, closed);
	}

	[Fact]
	public async Task CloseEmptyRooms_BeforeTheClose_AnnouncesTheClosingOnceAtTheWarningTime()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");
		var (room, opened) = await app.Lobby.ScopeTo(creator).OpenRoomAsync("test room", "", RoomSource.Game, false);
		Assert.True(opened.IsOk);
		Assert.True((await room!.ScopeTo(creator).Members.LeaveAsync()).IsOk);
		var closesAt = app.Clock.GetUtcNow() + Lobby.EmptyRoomTimeout;
		var announcedAtLeave = app.LobbyEvents.Events.OfType<LobbyRoomClosingAnnounced>().Count();

		app.Clock.Advance(Lobby.EmptyRoomTimeout - Lobby.EmptyRoomWarningBefore - OneSecond);
		app.Lobby.CloseEmptyRooms();
		var beforeWarning = app.LobbyEvents.Events.OfType<LobbyRoomClosingAnnounced>().Count();
		app.Clock.Advance(OneSecond);
		app.Lobby.CloseEmptyRooms();
		app.Lobby.CloseEmptyRooms();

		var announcements = app.LobbyEvents.Events.OfType<LobbyRoomClosingAnnounced>().ToList();
		Assert.Equal(announcedAtLeave, beforeWarning);
		Assert.Equal(announcedAtLeave + 1, announcements.Count);
		Assert.Equal(closesAt, announcements[^1].ClosesAt);
	}
}