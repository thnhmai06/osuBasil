using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;
using Basil.Infrastructure.Multiplayer.Handlers;
using Basil.Testing;

namespace Basil.Infrastructure.Multiplayer.Tests.Handlers;

public sealed class ListRoomsHandlerTests
{
	private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

	private static async Task<Room> OpenAsync(StableConnection creator, TestApplication app, string name,
		string password = "", bool isPrivate = false)
	{
		var (room, result) = await app.Lobby.ScopeTo(creator).OpenRoomAsync(name, password, RoomSource.Api, isPrivate);
		Assert.Equal(RoomOutcome.Ok, result);
		return room!;
	}

	private static async Task<IReadOnlyList<int>> ListAsync(TestApplication app, string query, Connection? actor = null,
		int offset = 0, int limit = 50)
	{
		var found = await Found(app, query, actor, offset, limit);
		return [.. found.Items.Select(room => room.Id)];
	}

	private static async Task<Page<Room>> Found(TestApplication app, string query, Connection? actor, int offset,
		int limit)
	{
		actor ??= await app.Users.OperateAsync(Ct);
		return await new ListRoomsHandler(app.Lobby).Handle(
			new ListRooms(actor, RoomQuery.Parse(query), new PageRequest(offset, limit)), Ct);
	}

	[Fact]
	public async Task NoFilter_ListsEveryRoomById()
	{
		using var app = new TestApplication();
		var alice = await RoomFixture.PlayerAsync(app, "Alice");
		var first = await OpenAsync(alice, app, "one");
		var second = await OpenAsync(alice, app, "two");

		Assert.Equal([first.Id, second.Id], await ListAsync(app, ""));
	}

	[Fact]
	public async Task FreeText_MatchesNameIgnoringCase()
	{
		using var app = new TestApplication();
		var alice = await RoomFixture.PlayerAsync(app, "Alice");
		var cup = await OpenAsync(alice, app, "Spring Cup");
		await OpenAsync(alice, app, "casual");

		Assert.Equal([cup.Id], await ListAsync(app, "spring cup"));
	}

	[Fact]
	public async Task Creator_ByNameAndById()
	{
		using var app = new TestApplication();
		var alice = await RoomFixture.PlayerAsync(app, "Alice");
		var bob = await RoomFixture.PlayerAsync(app, "Bob");
		var aliceRoom = await OpenAsync(alice, app, "a");
		var bobRoom = await OpenAsync(bob, app, "b");

		Assert.Equal([aliceRoom.Id], await ListAsync(app, "creator:alice"));
		Assert.Equal([bobRoom.Id], await ListAsync(app, $"creator:{bob.Record.Id}"));
	}

	[Fact]
	public async Task Host_ByNameAndById()
	{
		using var app = new TestApplication();
		var alice = await RoomFixture.PlayerAsync(app, "Alice");
		var bob = await RoomFixture.PlayerAsync(app, "Bob");
		var hosted = await OpenAsync(alice, app, "hosted by alice");
		await OpenAsync(bob, app, "hosted by bob");
		Assert.Same(alice, hosted.Authority.Host);

		Assert.Equal([hosted.Id], await ListAsync(app, "host:Alice"));
		Assert.Equal([hosted.Id], await ListAsync(app, $"host:{alice.Record.Id}"));
	}

	[Fact]
	public async Task Player_MatchesAnySeatedPlayer()
	{
		using var app = new TestApplication();
		var alice = await RoomFixture.PlayerAsync(app, "Alice");
		var bob = await RoomFixture.PlayerAsync(app, "Bob");
		var carol = await RoomFixture.PlayerAsync(app, "Carol");
		var room = await OpenAsync(alice, app, "a");
		await OpenAsync(carol, app, "c");
		Assert.Equal(RoomOutcome.Ok, await room.ScopeTo(bob).Members.JoinAsync(""));

		Assert.Equal([room.Id], await ListAsync(app, "player:bob"));
		Assert.Equal([room.Id], await ListAsync(app, $"player:{bob.Record.Id}"));
	}

	[Fact]
	public async Task Status_SeparatesOpenLockedAndPlaying()
	{
		using var app = new TestApplication();
		var alice = await RoomFixture.PlayerAsync(app, "Alice");
		var open = await OpenAsync(alice, app, "open");
		var locked = await OpenAsync(alice, app, "locked");
		var bob = await RoomFixture.PlayerAsync(app, "Bob");
		var playing = await OpenAsync(bob, app, "playing");
		Assert.Equal(RoomOutcome.Ok, await locked.ScopeTo(alice).Settings.SetLockedAsync(true));
		Assert.Equal(RoomOutcome.Ok, await playing.ScopeTo(bob).Settings.ConfigureAsync(
			new RoomSettingsChange(Beatmap: RoomFixture.Beatmap)));
		Assert.Equal(RoomOutcome.Ok, await playing.ScopeTo(bob).Rounds.StartAsync());
		Assert.True(playing.Rounds.InProgress);

		Assert.Equal([open.Id], await ListAsync(app, "status:open"));
		Assert.Equal([locked.Id], await ListAsync(app, "status:locked"));
		Assert.Equal([playing.Id], await ListAsync(app, "status:playing"));
	}

	[Fact]
	public async Task Mode_MatchesTheRoomsGameMode()
	{
		using var app = new TestApplication();
		var alice = await RoomFixture.PlayerAsync(app, "Alice");
		await OpenAsync(alice, app, "standard");
		var mania = await OpenAsync(alice, app, "mania");
		Assert.Equal(RoomOutcome.Ok, await mania.ScopeTo(alice).Settings.ConfigureAsync(
			new RoomSettingsChange(Mode: GameMode.Mania)));

		Assert.Equal([mania.Id], await ListAsync(app, "mode:3"));
	}

	[Fact]
	public async Task Password_YesAndNo()
	{
		using var app = new TestApplication();
		var alice = await RoomFixture.PlayerAsync(app, "Alice");
		var open = await OpenAsync(alice, app, "open");
		var secret = await OpenAsync(alice, app, "secret", "hunter2");

		Assert.Equal([secret.Id], await ListAsync(app, "password:yes"));
		Assert.Equal([open.Id], await ListAsync(app, "password:no"));
	}

	[Fact]
	public async Task Filters_CombineAsAnd()
	{
		using var app = new TestApplication();
		var alice = await RoomFixture.PlayerAsync(app, "Alice");
		var bob = await RoomFixture.PlayerAsync(app, "Bob");
		var wanted = await OpenAsync(alice, app, "Cup final", "pw");
		await OpenAsync(alice, app, "Cup open");
		await OpenAsync(bob, app, "Cup final", "pw");

		Assert.Equal([wanted.Id], await ListAsync(app, "cup creator:alice password:yes"));
	}

	[Fact]
	public async Task Paging_OffsetLimitAndHasNext()
	{
		using var app = new TestApplication();
		var alice = await RoomFixture.PlayerAsync(app, "Alice");
		var rooms = new List<Room>();
		for (var i = 0; i < 3; i++) rooms.Add(await OpenAsync(alice, app, $"room {i}"));

		var first = await Found(app, "", null, 0, 2);
		var rest = await Found(app, "", null, 2, 2);

		Assert.Equal([rooms[0].Id, rooms[1].Id], first.Items.Select(room => room.Id));
		Assert.True(first.HasNext);
		Assert.Equal([rooms[2].Id], rest.Items.Select(room => room.Id));
		Assert.False(rest.HasNext);
	}

	[Fact]
	public async Task PrivateRoom_ListedOnlyToThoseWhoMayView()
	{
		using var app = new TestApplication();
		var alice = await RoomFixture.PlayerAsync(app, "Alice");
		var outsider = await RoomFixture.PlayerAsync(app, "Outsider");
		var manager = await RoomFixture.PlayerAsync(app, "Manager",
			UserPrivilegesExtensions.Default | UserPrivileges.Tournament);
		var hidden = await OpenAsync(alice, app, "hidden", isPrivate: true);

		Assert.Empty(await ListAsync(app, "", outsider));
		Assert.Equal([hidden.Id], await ListAsync(app, "", alice));
		Assert.Equal([hidden.Id], await ListAsync(app, "", manager));
	}
}
