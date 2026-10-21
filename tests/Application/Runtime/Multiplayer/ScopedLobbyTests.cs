using Basil.Application.Runtime.Multiplayer;
using Basil.Application.Runtime.Multiplayer.Events;
using Basil.Application.Runtime.Multiplayer.Rooms;
using Basil.Application.Runtime.Sessions;
using Basil.Application.Runtime.Tests.Support;
using Basil.Domain;
using Basil.Domain.Permissions;

namespace Basil.Application.Runtime.Tests.Multiplayer;

/// <summary>
///     Opening rooms. The expected behaviour comes from AGENTS.md ("Authority") and
///     docs/for-developers/multiplayer.md ("Room lifecycle and concurrency"): a room needs its creator, a room
///     opened in game seats its creator, and an empty tournament room closes after fifteen minutes.
/// </summary>
public sealed class ScopedLobbyTests
{
	[Fact]
	public async Task OpenRoomAsync_WithoutRoomCreate_IsDeniedNamingRoomCreate()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator",
			PermissionsGroups.Default & ~Permissions.RoomCreate);

		var (room, result) = await OpenAsync(app, creator, isTournament: true);

		Assert.Null(room);
		Assert.False(result.IsOk);
		Assert.Equal(Permissions.RoomCreate, result.Missing);
	}

	[Fact]
	public async Task OpenRoomAsync_OpenedInGameWithoutRoomJoin_IsDeniedNamingRoomJoin()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator",
			PermissionsGroups.Default & ~Permissions.RoomJoin);

		var (room, result) = await OpenAsync(app, creator);

		Assert.Null(room);
		Assert.False(result.IsOk);
		Assert.Equal(Permissions.RoomJoin, result.Missing);
	}

	[Fact]
	public async Task OpenRoomAsync_OpenedInGame_SeatsTheCreatorAsTheFirstHost()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");

		var (room, result) = await OpenAsync(app, creator);

		Assert.True(result.IsOk);
		Assert.NotNull(room);
		Assert.Same(creator, room.Authority.Host);
		Assert.Equal(1, room.Slots.Find(creator)!.Index);
		var opened = Assert.IsType<LobbyRoomOpened>(app.LobbyEvents.Events[^1]);
		Assert.Same(creator, opened.Host);
		Assert.Null(opened.ClosesAt);
	}

	[Fact]
	public async Task OpenRoomAsync_ForATournamentRoomWhileTheCreatorsClientIsOffline_SeatsNobody()
	{
		using var app = new TestApplication();
		var creator = await app.CreateUserAsync("Creator", "password");
		var connection = await app.LoginAsync(creator);

		var (room, result) = await OpenAsync(app, connection, isTournament: true);

		Assert.True(result.IsOk);
		Assert.NotNull(room);
		Assert.Null(room.Authority.Host);
		Assert.DoesNotContain(room.Slots, slot => slot.Player is not null);
		var opened = Assert.IsType<LobbyRoomOpened>(app.LobbyEvents.Events[^1]);
		Assert.Null(opened.Host);
		Assert.NotNull(opened.ClosesAt);
	}

	[Fact]
	public async Task OpenRoomAsync_ForATournamentRoomWhileTheCreatorsClientIsOnline_SeatsTheCreatorAsHost()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");

		var (room, result) = await OpenAsync(app, creator, isTournament: true);

		Assert.True(result.IsOk);
		Assert.NotNull(room);
		Assert.Same(creator, room.Authority.Host);
		var opened = Assert.IsType<LobbyRoomOpened>(app.LobbyEvents.Events[^1]);
		Assert.Same(creator, opened.Host);
	}

	[Fact]
	public async Task OpenRoomAsync_ForATournamentRoomWhileTheCreatorIsInAnotherRoom_LeavesTheCreatorThere()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");
		var (first, _) = await OpenAsync(app, creator);

		var (second, result) = await OpenAsync(app, creator, "second", isTournament: true);

		Assert.True(result.IsOk);
		Assert.NotNull(second);
		Assert.Null(second.Authority.Host);
		Assert.Same(first, app.Lobby.RoomOf(creator));
		Assert.Equal(1, first!.Slots.Find(creator)!.Index);
	}

	[Fact]
	public async Task OpenRoomAsync_OpenedInGameWhileTheCreatorIsInAnotherRoom_IsRefused()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");
		var (first, _) = await OpenAsync(app, creator);
		Assert.True(first is not null);

		var (room, result) = await OpenAsync(app, creator, "second");

		Assert.Null(room);
		Assert.Equal(RoomOutcome.AlreadyInRoom, result.Outcome);
	}

	[Fact]
	public async Task OpenRoomAsync_AFifthTournamentRoom_IsRefused()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");
		for (var index = 0; index < 4; index++)
			Assert.True((await OpenAsync(app, creator, $"room {index}", isTournament: true)).Result.IsOk);

		var (room, result) = await OpenAsync(app, creator, "room 4", isTournament: true);

		Assert.Null(room);
		Assert.Equal(RoomOutcome.TooManyRooms, result.Outcome);
	}

	[Fact]
	public async Task OpenRoomAsync_WithRoomCreateUnlimited_OpensMoreThanFourTournamentRooms()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator",
			PermissionsGroups.Default | Permissions.RoomCreateUnlimited);
		for (var index = 0; index < 4; index++)
			Assert.True((await OpenAsync(app, creator, $"room {index}", isTournament: true)).Result.IsOk);

		var (room, result) = await OpenAsync(app, creator, "room 4", isTournament: true);

		Assert.True(result.IsOk);
		Assert.NotNull(room);
	}

	[Fact]
	public async Task OpenRoomAsync_WhileRoomCreationIsLocked_IsDeniedNamingRoomManageAny()
	{
		using var app = new TestApplication();
		await app.SetSettingsAsync(new ServerSettings
		{
			LockedCreation = ServerSettings.CreationLocks.Rooms
		});
		var creator = await RoomFixture.PlayerAsync(app, "Creator");

		var (room, result) = await OpenAsync(app, creator);

		Assert.Null(room);
		Assert.False(result.IsOk);
		Assert.Equal(Permissions.RoomManageAny, result.Missing);
	}

	[Fact]
	public async Task OpenRoomAsync_WhileRoomCreationIsLocked_AUserWhoManagesEveryRoomIsAllowed()
	{
		using var app = new TestApplication();
		await app.SetSettingsAsync(new ServerSettings
		{
			LockedCreation = ServerSettings.CreationLocks.Rooms
		});
		var creator = await RoomFixture.PlayerAsync(app, "Creator",
			PermissionsGroups.Default | Permissions.RoomManageAny);

		var (room, result) = await OpenAsync(app, creator);

		Assert.True(result.IsOk);
		Assert.NotNull(room);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public async Task OpenRoomAsync_WithAnInvalidName_IsRefused(string name)
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");

		var (room, result) = await OpenAsync(app, creator, name);

		Assert.Null(room);
		Assert.Equal(RoomOutcome.InvalidSettings, result.Outcome);
	}

	[Fact]
	public async Task OpenRoomAsync_GivesEveryRoomACreator()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Creator", "password");
		var creator = await app.LoginAsync(user);

		var (room, result) = await OpenAsync(app, creator, isTournament: true);

		Assert.True(result.IsOk);
		Assert.Equal(user, room!.Authority.Creator);
	}

	[Fact]
	public async Task OpenRoomAsync_GivesEveryOpenRoomItsOwnId()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");

		var (first, _) = await OpenAsync(app, creator, "first");
		var (second, _) = await OpenAsync(app, creator, "second", isTournament: true);

		Assert.NotEqual(first!.Id, second!.Id);
	}

	[Fact]
	public async Task OpenRoomAsync_AfterARoomClosed_HandsOutItsIdAgain()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");
		var (first, _) = await OpenAsync(app, creator, "first");
		Assert.True((await first!.ScopeTo(creator).CloseAsync(TestContext.Current.CancellationToken)).IsOk);

		var (second, result) = await OpenAsync(app, creator, "second");

		Assert.True(result.IsOk);
		Assert.Equal(first.Id, second!.Id);
	}

	[Fact]
	public async Task OpenRoomAsync_ATournamentRoomNobodySitsIn_ClosesAfterFifteenMinutes()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Creator", "password");
		var connection = await app.LoginAsync(user);
		var (room, result) = await OpenAsync(app, connection, isTournament: true);
		Assert.True(result.IsOk);
		var opened = Assert.IsType<LobbyRoomOpened>(app.LobbyEvents.Events[^1]);
		Assert.Equal(app.Clock.GetUtcNow().AddMinutes(15), opened.ClosesAt);

		app.Clock.Advance(TimeSpan.FromMinutes(14) + TimeSpan.FromSeconds(59));

		Assert.False(room!.IsClosed);

		app.Clock.Advance(TimeSpan.FromSeconds(1));

		Assert.True(room.IsClosed);
		Assert.IsType<LobbyRoomClosed>(app.LobbyEvents.Events[^1]);
	}

	[Fact]
	public async Task OpenRoomAsync_ATournamentRoomSomebodySitsIn_StaysOpen()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Creator", "password");
		var connection = await app.LoginAsync(user);
		var (room, result) = await OpenAsync(app, connection, isTournament: true);
		Assert.True(result.IsOk);
		var player = await RoomFixture.PlayerAsync(app, "Player");
		Assert.True((await room!.ScopeTo(player).Members.JoinAsync("", TestContext.Current.CancellationToken)).IsOk);

		app.Clock.Advance(TimeSpan.FromMinutes(16));

		Assert.False(room.IsClosed);
		Assert.DoesNotContain(app.LobbyEvents.Events, lobbyEvent => lobbyEvent is LobbyRoomClosed);
	}

	[Fact]
	public async Task OpenRoomAsync_ATournamentRoomThatBecomesEmptyAgain_StartsTheDelayOver()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Creator", "password");
		var connection = await app.LoginAsync(user);
		var (room, result) = await OpenAsync(app, connection, isTournament: true);
		Assert.True(result.IsOk);
		var player = await RoomFixture.PlayerAsync(app, "Player");
		Assert.True((await room!.ScopeTo(player).Members.JoinAsync("", TestContext.Current.CancellationToken)).IsOk);
		Assert.True((await room.ScopeTo(player).Members.LeaveAsync(TestContext.Current.CancellationToken)).IsOk);

		app.Clock.Advance(TimeSpan.FromMinutes(14));

		Assert.False(room.IsClosed);

		app.Clock.Advance(TimeSpan.FromMinutes(1));

		Assert.True(room.IsClosed);
	}

	[Fact]
	public async Task Find_AnIdNoRoomHas_ReturnsNothing()
	{
		using var app = new TestApplication();
		var viewer = await RoomFixture.PlayerAsync(app, "Viewer");

		Assert.Null(app.Lobby.ScopeTo(viewer).Find(99));
	}

	[Fact]
	public async Task Find_APublicRoom_ReturnsItToAnyone()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");
		var (room, _) = await OpenAsync(app, creator);
		var stranger = await RoomFixture.PlayerAsync(app, "Stranger");

		Assert.Same(room, app.Lobby.ScopeTo(stranger).Find(room!.Id));
	}

	[Fact]
	public async Task Find_ARoomThatClosed_ReturnsNothing()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");
		var (room, _) = await OpenAsync(app, creator, isTournament: true);
		Assert.True((await room!.ScopeTo(creator).CloseAsync(TestContext.Current.CancellationToken)).IsOk);

		Assert.Null(app.Lobby.ScopeTo(creator).Find(room.Id));
	}

	[Fact]
	public async Task Find_APrivateRoom_IsHiddenFromAStranger()
	{
		using var app = new TestApplication();
		var (room, _) = await OpenPrivateAsync(app);
		var stranger = await RoomFixture.PlayerAsync(app, "Stranger");

		Assert.Null(app.Lobby.ScopeTo(stranger).Find(room.Id));
	}

	[Fact]
	public async Task Find_APrivateRoom_IsShownToItsCreator()
	{
		using var app = new TestApplication();
		var (room, creator) = await OpenPrivateAsync(app);

		Assert.Same(room, app.Lobby.ScopeTo(creator).Find(room.Id));
	}

	[Fact]
	public async Task Find_APrivateRoom_IsShownToItsReferees()
	{
		using var app = new TestApplication();
		var (room, creator) = await OpenPrivateAsync(app);
		var referee = await RoomFixture.PlayerAsync(app, "Referee");
		Assert.True((await room.ScopeTo(creator).Authority
			.AddRefereeAsync(referee.Record, TestContext.Current.CancellationToken)).IsOk);

		Assert.Same(room, app.Lobby.ScopeTo(referee).Find(room.Id));
	}

	[Fact]
	public async Task Find_APrivateRoom_IsShownToAPlayerSeatedInIt()
	{
		using var app = new TestApplication();
		var (room, creator) = await OpenPrivateAsync(app);
		var player = await RoomFixture.PlayerAsync(app, "Player");
		Assert.True((await room.ScopeTo(creator).Members.InviteAsync(player.User,
			TestContext.Current.CancellationToken)).IsOk);
		Assert.True((await room.ScopeTo(player).Members.JoinAsync("", TestContext.Current.CancellationToken)).IsOk);

		Assert.Same(room, app.Lobby.ScopeTo(player).Find(room.Id));
	}

	[Fact]
	public async Task Find_APrivateRoom_IsShownToAnObserverOfIt()
	{
		using var app = new TestApplication();
		var (room, _) = await OpenPrivateAsync(app);
		var observer = (TourneyConnection)await app.LoginAsync(
			await app.CreateUserAsync("Observer", "password", PermissionsGroups.Default | Permissions.RoomObserve),
			ConnectionType.Tourney);
		Assert.True((await room.ScopeTo(observer).Members.ObserverJoinAsync(TestContext.Current.CancellationToken))
			.IsOk);

		Assert.Same(room, app.Lobby.ScopeTo(observer).Find(room.Id));
	}

	[Fact]
	public async Task Find_APrivateRoom_IsShownToAUserWhoMayViewPrivateRooms()
	{
		using var app = new TestApplication();
		var (room, _) = await OpenPrivateAsync(app);
		var viewer = await RoomFixture.PlayerAsync(app, "Viewer",
			PermissionsGroups.Default | Permissions.RoomViewPrivate);

		Assert.Same(room, app.Lobby.ScopeTo(viewer).Find(room.Id));
	}

	private static async Task<(Room Room, BanchoConnection Creator)> OpenPrivateAsync(TestApplication app)
	{
		var creator = await RoomFixture.PlayerAsync(app, "Creator");
		var (room, result) = await app.Lobby.ScopeTo(creator).OpenRoomAsync("private room", "", false, true,
			cancellationToken: TestContext.Current.CancellationToken);
		Assert.True(result.IsOk);
		return (room!, creator);
	}

	private static Task<(Room? Room, Result<RoomOutcome> Result)> OpenAsync(TestApplication app, Connection by,
		string name = "test room", bool isTournament = false, string password = "")
	{
		return app.Lobby.ScopeTo(by).OpenRoomAsync(name, password, isTournament, false,
			cancellationToken: TestContext.Current.CancellationToken);
	}
}