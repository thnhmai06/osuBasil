using Basil.Application.Common;
using Basil.Application.Content.Persistence;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Authentication;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;
using Basil.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Multiplayer.Tests.Capabilities;

/// <summary>
///     Opening rooms. The expected behaviour comes from AGENTS.md ("Authority") and
///     docs/for-developers/multiplayer.md ("Room lifecycle and concurrency"): a room needs its creator, a room
///     opened in game seats its creator, and every room is limited by the same number of rooms per creator.
/// </summary>
public sealed class ScopedLobbyTests
{
	[Fact]
	public async Task OpenRoomAsync_WithoutRoomCreate_IsDeniedNamingRoomCreate()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator",
			UserPrivilegesExtensions.Default & ~UserPrivileges.Player);

		var (room, result) = await OpenAsync(app, creator, source: RoomSource.Api);

		Assert.Null(room);
		Assert.False(result.IsOk);
		Assert.Equal(UserPrivileges.Player, result.Missing);
	}

	[Fact]
	public async Task OpenRoomAsync_OpenedInGameWithoutRoomJoin_IsDeniedNamingRoomJoin()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator",
			UserPrivilegesExtensions.Default & ~UserPrivileges.Player);

		var (room, result) = await OpenAsync(app, creator);

		Assert.Null(room);
		Assert.False(result.IsOk);
		Assert.Equal(UserPrivileges.Player, result.Missing);
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
	public async Task OpenRoomAsync_OpenedThroughTheApiWhileTheCreatorsClientIsOffline_SeatsNobody()
	{
		using var app = new TestApplication();
		var creator = await app.CreateUserAsync("Creator", "password");
		var connection = await app.LoginAsync(creator);

		var (room, result) = await OpenAsync(app, connection, source: RoomSource.Api);

		Assert.True(result.IsOk);
		Assert.NotNull(room);
		Assert.Null(room.Authority.Host);
		Assert.DoesNotContain(room.Slots, slot => slot.Player is not null);
		var opened = Assert.IsType<LobbyRoomOpened>(app.LobbyEvents.Events[^1]);
		Assert.Null(opened.Host);
		Assert.NotNull(opened.ClosesAt);
	}

	[Fact]
	public async Task OpenRoomAsync_OpenedThroughTheApiWhileTheCreatorsClientIsOnline_SeatsTheCreatorAsHost()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");

		var (room, result) = await OpenAsync(app, creator, source: RoomSource.Api);

		Assert.True(result.IsOk);
		Assert.NotNull(room);
		Assert.Same(creator, room.Authority.Host);
		var opened = Assert.IsType<LobbyRoomOpened>(app.LobbyEvents.Events[^1]);
		Assert.Same(creator, opened.Host);
	}

	[Fact]
	public async Task OpenRoomAsync_OpenedThroughTheApiWhileTheCreatorIsInAnotherRoom_LeavesTheCreatorThere()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");
		var (first, _) = await OpenAsync(app, creator);

		var (second, result) = await OpenAsync(app, creator, "second", RoomSource.Api);

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
	public async Task OpenRoomAsync_AFifthRoomOpenedThroughTheApi_IsRefused()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");
		for (var index = 0; index < 4; index++)
			Assert.True((await OpenAsync(app, creator, $"room {index}", RoomSource.Api)).Result.IsOk);

		var (room, result) = await OpenAsync(app, creator, "room 4", RoomSource.Api);

		Assert.Null(room);
		Assert.Equal(RoomOutcome.TooManyRooms, result.Outcome);
	}

	[Fact]
	public async Task OpenRoomAsync_AFifthRoomOpenedInGame_IsRefused()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");
		for (var index = 0; index < 4; index++)
			Assert.True((await OpenAsync(app, creator, $"room {index}", RoomSource.Api)).Result.IsOk);

		var (room, result) = await OpenAsync(app, creator, "room 4");

		Assert.Null(room);
		Assert.Equal(RoomOutcome.TooManyRooms, result.Outcome);
	}

	[Fact]
	public async Task OpenRoomAsync_WithRoomCreateUnlimited_OpensMoreThanFourRooms()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator",
			UserPrivilegesExtensions.Default | UserPrivileges.Tournament);
		for (var index = 0; index < 4; index++)
			Assert.True((await OpenAsync(app, creator, $"room {index}", RoomSource.Api)).Result.IsOk);

		var (room, result) = await OpenAsync(app, creator, "room 4", RoomSource.Api);

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
			UserPrivilegesExtensions.Default | UserPrivileges.Tournament);

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

		var (room, result) = await OpenAsync(app, creator, source: RoomSource.Api);

		Assert.True(result.IsOk);
		Assert.Equal(user, room!.Authority.Creator);
	}

	[Fact]
	public async Task OpenRoomAsync_GivesEveryOpenRoomItsOwnId()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");

		var (first, _) = await OpenAsync(app, creator, "first");
		var (second, _) = await OpenAsync(app, creator, "second", RoomSource.Api);

		Assert.NotEqual(first!.Id, second!.Id);
	}

	[Fact]
	public async Task OpenRoomAsync_AfterARoomClosed_HandsOutItsIdAgain()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");
		var (first, _) = await OpenAsync(app, creator, "first");
		Assert.True((await first!.ScopeTo(creator).CloseAsync()).IsOk);

		var (second, result) = await OpenAsync(app, creator, "second");

		Assert.True(result.IsOk);
		Assert.Equal(first.Id, second!.Id);
	}

	private static Task<(Room? Room, Result<RoomOutcome> Result)> OpenAsync(TestApplication app, Connection actor,
		string name = "test room", RoomSource source = RoomSource.Game, string password = "")
	{
		return app.Lobby.ScopeTo(actor).OpenRoomAsync(name, password, source, false);
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
			UserPrivilegesExtensions.Default | UserPrivileges.Tournament));

		Assert.True(await app.Lobby.ScopeTo(viewer).CanViewAsync(match, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task CanViewAsync_APrivateMatch_IsOpenToAHolderOfRoomManageAny()
	{
		using var app = new TestApplication();
		var match = await MatchAsync(app, await app.CreateUserAsync("Creator", "password"), true);
		var manager = await app.LoginAsync(await app.CreateUserAsync("Manager", "password",
			UserPrivilegesExtensions.Default | UserPrivileges.Tournament));

		Assert.True(await app.Lobby.ScopeTo(manager).CanViewAsync(match, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task CanViewAsync_APlayerWhoLeftAPrivateOpenRoom_LosesTheViewWhileItStaysOpen()
	{
		using var app = new TestApplication();
		var creator = await app.LoginAsync(await app.CreateUserAsync("Creator", "password"), SessionType.Stable);
		var (room, result) = await OpenAsync(app, creator);
		Assert.True(result.IsOk);
		Assert.True((await room!.ScopeTo(creator).Settings.ConfigureAsync(new RoomSettingsChange(IsPrivate: true))).IsOk);
		var player = await RoomFixture.PlayerAsync(app, "Player");
		Assert.True((await room.ScopeTo(player).Members.JoinAsync("")).IsOk);
		Assert.True((await room.ScopeTo(player).Members.LeaveAsync()).IsOk);
		// The stored history shows the join (the store's copy of the room can say it is visible).
		await app.Provider.GetRequiredService<IMatchEventRepository>().RecordAsync(
			new MatchEvent(room.Match, MatchEventType.PlayerJoined, app.Clock.GetUtcNow(), null, player.Record));
		Assert.True(await app.Provider.GetRequiredService<IMatchRepository>().IsVisibleToAsync(room.Match,
			player.Record,
			TestContext.Current.CancellationToken), "precondition: the stored history shows the player");

		Assert.False(await app.Lobby.ScopeTo(player).CanViewAsync(room.Match, TestContext.Current.CancellationToken));
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
		var (room, opened) = await app.Lobby.ScopeTo(creator).OpenRoomAsync("secret room", "", RoomSource.Game, true);
		Assert.True(opened.IsOk);
		var player = await RoomFixture.PlayerAsync(app, "Player");
		Assert.True((await room!.ScopeTo(player).Members.JoinAsync("")).IsOk);

		Assert.True(await app.Lobby.ScopeTo(player).CanViewAsync(room.Match, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task CanViewAsync_APrivateOpenRoom_IsClosedToAStrangerWhoIsNotInIt()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");
		var (room, opened) = await app.Lobby.ScopeTo(creator).OpenRoomAsync("secret room", "", RoomSource.Game, true);
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
		});
	}

	private static Task RecordAsync(TestApplication app, MatchRecord match, MatchEventType type, UserRecord target)
	{
		return app.Provider.GetRequiredService<IMatchEventRepository>().RecordAsync(
			new MatchEvent(match, type, app.Clock.GetUtcNow(), Target: target));
	}

	#endregion
}