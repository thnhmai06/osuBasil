using Basil.Application.Content.Persistence;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Common;
using Basil.Application.Users.Connections;
using Basil.Application.Multiplayer.Runtime;
using Basil.Application.Multiplayer.Runtime.Events;
using Basil.Application.Multiplayer.Runtime.Rooms;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;
using Basil.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Multiplayer.Tests.Runtime;

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
			UserPermissionsGroups.Default & ~UserPermissions.RoomCreate);

		var (room, result) = await OpenAsync(app, creator, isTournament: true);

		Assert.Null(room);
		Assert.False(result.IsOk);
		Assert.Equal(UserPermissions.RoomCreate, result.Missing);
	}

	[Fact]
	public async Task OpenRoomAsync_OpenedInGameWithoutRoomJoin_IsDeniedNamingRoomJoin()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator",
			UserPermissionsGroups.Default & ~UserPermissions.RoomJoin);

		var (room, result) = await OpenAsync(app, creator);

		Assert.Null(room);
		Assert.False(result.IsOk);
		Assert.Equal(UserPermissions.RoomJoin, result.Missing);
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

		var (second, result) = await OpenAsync(app, creator, "second", true);

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
			Assert.True((await OpenAsync(app, creator, $"room {index}", true)).Result.IsOk);

		var (room, result) = await OpenAsync(app, creator, "room 4", true);

		Assert.Null(room);
		Assert.Equal(RoomOutcome.TooManyRooms, result.Outcome);
	}

	[Fact]
	public async Task OpenRoomAsync_WithRoomCreateUnlimited_OpensMoreThanFourTournamentRooms()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator",
			UserPermissionsGroups.Default | UserPermissions.RoomManageCreateUnlimited);
		for (var index = 0; index < 4; index++)
			Assert.True((await OpenAsync(app, creator, $"room {index}", true)).Result.IsOk);

		var (room, result) = await OpenAsync(app, creator, "room 4", true);

		Assert.True(result.IsOk);
		Assert.NotNull(room);
	}

	[Fact]
	public async Task OpenRoomAsync_WhileRoomCreationIsLocked_ReturnsLocked()
	{
		using var app = new TestApplication();
		await app.SetSettingsAsync(new ServerSettings
		{
			Locks = Locks.Rooms
		});
		var creator = await RoomFixture.PlayerAsync(app, "Creator");

		var (room, result) = await OpenAsync(app, creator);

		Assert.Null(room);
		Assert.False(result.IsOk);
		Assert.Equal(RoomOutcome.Locked, result.Outcome);
		Assert.Null(result.Missing);
	}

	[Fact]
	public async Task OpenRoomAsync_WhileRoomCreationIsLocked_AUserWhoManagesEveryRoomIsAllowed()
	{
		using var app = new TestApplication();
		await app.SetSettingsAsync(new ServerSettings
		{
			Locks = Locks.Rooms
		});
		var creator = await RoomFixture.PlayerAsync(app, "Creator",
			UserPermissionsGroups.Default | UserPermissions.RoomManageAny);

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
		var (second, _) = await OpenAsync(app, creator, "second", true);

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

	private static Task<(Room? Room, Result<RoomOutcome> Result)> OpenAsync(TestApplication app, Connection by,
		string name = "test room", bool isTournament = false, string password = "")
	{
		return app.Lobby.ScopeTo(by).OpenRoomAsync(name, password, isTournament, false,
			cancellationToken: TestContext.Current.CancellationToken);
	}

	#region CanViewAsync

	[Fact]
	public async Task CanViewAsync_APublicMatch_IsOpenToAnyone()
	{
		using var app = new TestApplication();
		var match = await MatchAsync(app, await app.CreateUserAsync("Creator", "password"), false);
		var stranger = await app.LoginAsync(await app.CreateUserAsync("Stranger", "password"));

		Assert.True(await app.Lobby.ScopeTo(stranger).CanViewAsync(match, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task CanViewAsync_APrivateMatch_IsClosedToAStranger()
	{
		using var app = new TestApplication();
		var match = await MatchAsync(app, await app.CreateUserAsync("Creator", "password"), true);
		var stranger = await app.LoginAsync(await app.CreateUserAsync("Stranger", "password"));

		Assert.False(await app.Lobby.ScopeTo(stranger).CanViewAsync(match, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task CanViewAsync_APrivateMatch_IsOpenToAHolderOfRoomViewPrivate()
	{
		using var app = new TestApplication();
		var match = await MatchAsync(app, await app.CreateUserAsync("Creator", "password"), true);
		var viewer = await app.LoginAsync(await app.CreateUserAsync("Viewer", "password",
			UserPermissionsGroups.Default | UserPermissions.RoomManageViewPrivate));

		Assert.True(await app.Lobby.ScopeTo(viewer).CanViewAsync(match, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task CanViewAsync_APrivateMatch_IsOpenToAHolderOfRoomManageAny()
	{
		using var app = new TestApplication();
		var match = await MatchAsync(app, await app.CreateUserAsync("Creator", "password"), true);
		var manager = await app.LoginAsync(await app.CreateUserAsync("Manager", "password",
			UserPermissionsGroups.Default | UserPermissions.RoomManageAny));

		Assert.True(await app.Lobby.ScopeTo(manager).CanViewAsync(match, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task CanViewAsync_RoomViewPrivateSuspendedByARestriction_DoesNotOpenAPrivateMatch()
	{
		using var app = new TestApplication();
		var match = await MatchAsync(app, await app.CreateUserAsync("Creator", "password"), true);
		var user = await app.CreateUserAsync("Viewer", "password",
			UserPermissionsGroups.Default | UserPermissions.RoomManageViewPrivate);
		app.AddRestriction(user, UserPermissions.RoomManageViewPrivate);
		var viewer = await app.LoginAsync(user);

		Assert.False(await app.Lobby.ScopeTo(viewer).CanViewAsync(match, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task CanViewAsync_APrivateMatch_IsOpenToItsCreator()
	{
		using var app = new TestApplication();
		var creator = await app.CreateUserAsync("Creator", "password");
		var match = await MatchAsync(app, creator, true);
		var connection = await app.LoginAsync(creator);

		Assert.True(await app.Lobby.ScopeTo(connection).CanViewAsync(match, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task CanViewAsync_APrivateMatch_IsOpenToAPlayerWhoJoined()
	{
		using var app = new TestApplication();
		var match = await MatchAsync(app, await app.CreateUserAsync("Creator", "password"), true);
		var player = await app.CreateUserAsync("Player", "password");
		await RecordAsync(app, match, MatchEventType.PlayerJoined, player);
		var connection = await app.LoginAsync(player);

		Assert.True(await app.Lobby.ScopeTo(connection).CanViewAsync(match, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task CanViewAsync_APrivateMatch_IsOpenToACurrentReferee()
	{
		using var app = new TestApplication();
		var match = await MatchAsync(app, await app.CreateUserAsync("Creator", "password"), true);
		var referee = await app.CreateUserAsync("Referee", "password");
		await RecordAsync(app, match, MatchEventType.RefAdded, referee);
		var connection = await app.LoginAsync(referee);

		Assert.True(await app.Lobby.ScopeTo(connection).CanViewAsync(match, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task CanViewAsync_APrivateMatch_IsClosedToARemovedReferee()
	{
		using var app = new TestApplication();
		var match = await MatchAsync(app, await app.CreateUserAsync("Creator", "password"), true);
		var referee = await app.CreateUserAsync("Referee", "password");
		await RecordAsync(app, match, MatchEventType.RefAdded, referee);
		await RecordAsync(app, match, MatchEventType.RefRemoved, referee);
		var connection = await app.LoginAsync(referee);

		Assert.False(await app.Lobby.ScopeTo(connection).CanViewAsync(match, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task CanViewAsync_APrivateOpenRoom_IsOpenToAPlayerSeatedInItBeforeItsHistoryIsStored()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");
		var (room, opened) = await app.Lobby.ScopeTo(creator).OpenRoomAsync("secret room", "", false, true,
			cancellationToken: TestContext.Current.CancellationToken);
		Assert.True(opened.IsOk);
		var player = await RoomFixture.PlayerAsync(app, "Player");
		Assert.True((await room!.ScopeTo(player).Members.JoinAsync("", TestContext.Current.CancellationToken)).IsOk);

		Assert.True(await app.Lobby.ScopeTo(player).CanViewAsync(room.Match, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task CanViewAsync_APrivateOpenRoom_IsClosedToAStrangerWhoIsNotInIt()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");
		var (room, opened) = await app.Lobby.ScopeTo(creator).OpenRoomAsync("secret room", "", false, true,
			cancellationToken: TestContext.Current.CancellationToken);
		Assert.True(opened.IsOk);
		var stranger = await RoomFixture.PlayerAsync(app, "Stranger");

		Assert.False(await app.Lobby.ScopeTo(stranger)
			.CanViewAsync(room!.Match, TestContext.Current.CancellationToken));
	}

	private static async Task<MatchRecord> MatchAsync(TestApplication app, UserRecord creator, bool isPrivate)
	{
		return await app.Provider.GetRequiredService<IMatchRepository>().CreateAsync(new MatchData
		{
			Name = "match",
			Creator = creator,
			StartedAt = app.Clock.GetUtcNow(),
			EndedAt = null,
			IsPrivate = isPrivate
		}, TestContext.Current.CancellationToken);
	}

	private static Task RecordAsync(TestApplication app, MatchRecord match, MatchEventType type, UserRecord target)
	{
		return app.Provider.GetRequiredService<IMatchEventRepository>().RecordAsync(
			new MatchEvent(match, type, app.Clock.GetUtcNow(), Target: target),
			TestContext.Current.CancellationToken);
	}

	#endregion
}
