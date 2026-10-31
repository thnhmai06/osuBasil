using Basil.Bot.Commands.Mp;
using Basil.Bot.Tests.Support;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Bot.Tests.Commands.Mp;

/// <summary>
///     The commands that open, enter, target and close rooms: <c>!mp make</c>, <c>!mp makeprivate</c>, <c>!mp join</c>,
///     <c>!mp in</c> and <c>!mp close</c>. Expected replies are named by the reply that owns the wording.
/// </summary>
public sealed class MpLifecycleTests
{
	[Fact]
	public async Task Make_ByAUserWithAClient_SaysTheMatchWasCreatedAsPublic()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		var (succeeded, said) = await bot.TypeAsync(typist, "#osu", "!mp make Finals");

		var room = bot.App.Lobby.Rooms.Values.Single();
		Assert.True(succeeded);
		Assert.Equal([bot.Text(MpReplies.CreatedMatch, new { room = room.Id, name = "Finals", privacy = "public" })],
			said.Here);
	}

	[Fact]
	public async Task Make_ByAUserWithAClient_OpensAPublicTournamentRoomCreatedByTheTypist()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		await bot.TypeAsync(typist, "#osu", "!mp make Finals");

		var room = Assert.Single(bot.App.Lobby.Rooms.Values);
		Assert.True(room.IsTournament);
		Assert.Equal(typist.Record, room.Authority.Creator);
		Assert.False(room.Settings.IsPrivate);
		Assert.Equal("Finals", room.Settings.Name);
	}

	[Fact]
	public async Task Make_ByAnOnlineCreatorInNoRoom_SeatsTheirClientAsHost()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		await bot.TypeAsync(typist, "#osu", "!mp make Finals");

		var room = Assert.Single(bot.App.Lobby.Rooms.Values);
		Assert.NotNull(room.Slots.Find(typist));
		Assert.Same(typist, room.Authority.Host);
	}

	[Fact]
	public async Task Make_MakesTheTypistTargetTheNewRoom()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		await bot.TypeAsync(typist, "#osu", "!mp make Finals");

		var room = Assert.Single(bot.App.Lobby.Rooms.Values);
		Assert.Same(room, bot.Targets.Get(typist.Record.Id));
	}

	[Fact]
	public async Task MakePrivate_SaysTheMatchWasCreatedAsPrivate()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		var (succeeded, said) = await bot.TypeAsync(typist, "#osu", "!mp makeprivate Finals");

		var room = bot.App.Lobby.Rooms.Values.Single();
		Assert.True(succeeded);
		Assert.Equal([bot.Text(MpReplies.CreatedMatch, new { room = room.Id, name = "Finals", privacy = "private" })],
			said.Here);
	}

	[Fact]
	public async Task MakePrivate_OpensAPrivateRoom()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		await bot.TypeAsync(typist, "#osu", "!mp makeprivate Finals");

		Assert.True(Assert.Single(bot.App.Lobby.Rooms.Values).Settings.IsPrivate);
	}

	[Fact]
	public async Task Make_WithoutAName_NamesTheRoomAfterTheTypist()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		await bot.TypeAsync(typist, "#osu", "!mp make");

		var room = Assert.Single(bot.App.Lobby.Rooms.Values);
		Assert.Equal(bot.Text(MpReplies.DefaultRoomName, new { name = "Typist" }), room.Settings.Name);
	}

	[Fact]
	public async Task Make_WithANameLongerThan50Characters_KeepsTheFirst50()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");
		var name = string.Concat(Enumerable.Repeat("abcdefghij", 6));

		await bot.TypeAsync(typist, "#osu", $"!mp make {name}");

		var room = Assert.Single(bot.App.Lobby.Rooms.Values);
		Assert.Equal(name[..50], room.Settings.Name);
	}

	[Fact]
	public async Task Make_WithoutTheRoomCreatePermission_SaysWhichPermissionIsMissingAndOpensNothing()
	{
		using var bot = new BotHarness();
		var typist =
			await RoomFixture.PlayerAsync(bot.App, "Typist",
				UserPermissionsGroups.Default & ~UserPermissions.RoomCreate);

		var (succeeded, said) = await bot.TypeAsync(typist, "#osu", "!mp make Finals");

		Assert.False(succeeded);
		Assert.Equal([bot.Text(MpReplies.MissingPermissions, new { permissions = "RoomCreate" })], said.Here);
		Assert.Empty(bot.App.Lobby.Rooms);
	}

	[Theory]
	[InlineData("!mp join")]
	[InlineData("!mp join abc")]
	public async Task Join_WithoutANumericId_SaysHowToUseItAndFails(string message)
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		var (succeeded, said) = await bot.TypeAsync(typist, "#osu", message);

		Assert.False(succeeded);
		Assert.Equal([bot.Text(MpReplies.JoinUsage)], said.Here);
	}

	[Fact]
	public async Task Join_ForAnIdNoRoomHas_SaysThereIsNoActiveMatch()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		var (succeeded, said) = await bot.TypeAsync(typist, "#osu", "!mp join 99");

		Assert.False(succeeded);
		Assert.Equal([bot.Text(MpReplies.NoActiveRoomWithId, new { room = 99 })], said.Here);
	}

	[Fact]
	public async Task Join_ForAPublicRoom_SaysTheTypistJoined()
	{
		using var bot = new BotHarness();
		var owner = await RoomFixture.PlayerAsync(bot.App, "Owner");
		var room = await bot.MakeAsync(owner, "Finals");
		var joiner = await RoomFixture.PlayerAsync(bot.App, "Joiner");

		var (succeeded, said) = await bot.TypeAsync(joiner, "#osu", $"!mp join {room.Id}");

		Assert.True(succeeded);
		Assert.Equal([bot.Text(MpReplies.JoinedMatch, new { room = room.Id, name = "Finals" })], said.Here);
	}

	[Fact]
	public async Task Join_ForAPublicRoom_SeatsTheTypist()
	{
		using var bot = new BotHarness();
		var owner = await RoomFixture.PlayerAsync(bot.App, "Owner");
		var room = await bot.MakeAsync(owner, "Finals");
		var joiner = await RoomFixture.PlayerAsync(bot.App, "Joiner");

		await bot.TypeAsync(joiner, "#osu", $"!mp join {room.Id}");

		Assert.NotNull(room.Slots.Find(joiner.Record));
	}

	[Fact]
	public async Task Join_ForAPublicRoom_MakesTheTypistTargetIt()
	{
		using var bot = new BotHarness();
		var owner = await RoomFixture.PlayerAsync(bot.App, "Owner");
		var room = await bot.MakeAsync(owner, "Finals");
		var joiner = await RoomFixture.PlayerAsync(bot.App, "Joiner");

		await bot.TypeAsync(joiner, "#osu", $"!mp join {room.Id}");

		Assert.Same(room, bot.Targets.Get(joiner.Record.Id));
	}

	[Fact]
	public async Task Join_WithTheRoomsPassword_SeatsTheTypist()
	{
		using var bot = new BotHarness();
		var owner = await RoomFixture.PlayerAsync(bot.App, "Owner");
		var room = await bot.MakeAsync(owner, "Finals");
		Assert.True((await bot.TypeAsync(owner, "#osu", "!mp password secret")).Succeeded);
		var joiner = await RoomFixture.PlayerAsync(bot.App, "Joiner");

		var (succeeded, said) = await bot.TypeAsync(joiner, "#osu", $"!mp join {room.Id} secret");

		Assert.True(succeeded);
		Assert.Equal([bot.Text(MpReplies.JoinedMatch, new { room = room.Id, name = "Finals" })], said.Here);
		Assert.NotNull(room.Slots.Find(joiner.Record));
	}

	[Theory]
	[InlineData("")]
	[InlineData(" wrong")]
	public async Task Join_WithAWrongOrMissingPassword_SaysItIsIncorrectAndDoesNotSeatTheTypist(string suffix)
	{
		using var bot = new BotHarness();
		var owner = await RoomFixture.PlayerAsync(bot.App, "Owner");
		var room = await bot.MakeAsync(owner, "Finals");
		Assert.True((await bot.TypeAsync(owner, "#osu", "!mp password secret")).Succeeded);
		var joiner = await RoomFixture.PlayerAsync(bot.App, "Joiner");

		var (succeeded, said) = await bot.TypeAsync(joiner, "#osu", $"!mp join {room.Id}{suffix}");

		Assert.False(succeeded);
		Assert.Equal([bot.Text(MpReplies.IncorrectPassword)], said.Here);
		Assert.Null(room.Slots.Find(joiner.Record));
	}

	[Fact]
	public async Task Join_ForAPrivateRoomOfAStranger_JoinsItBecausePrivateOnlyKeepsARoomOutOfListings()
	{
		using var bot = new BotHarness();
		var owner = await RoomFixture.PlayerAsync(bot.App, "Owner");
		var room = await bot.MakeAsync(owner, "Secret", true);
		var stranger = await RoomFixture.PlayerAsync(bot.App, "Stranger");

		var (succeeded, said) = await bot.TypeAsync(stranger, "#osu", $"!mp join {room.Id}");

		Assert.True(succeeded);
		Assert.Equal([bot.Text(MpReplies.JoinedMatch, new { room = room.Id, name = "Secret" })], said.Here);
		Assert.NotNull(room.Slots.Find(stranger.Record));
	}

	[Fact]
	public async Task In_TypedInAChannel_SaysItOnlyWorksInPrivateAndFails()
	{
		using var bot = new BotHarness();
		var owner = await RoomFixture.PlayerAsync(bot.App, "Owner");
		var room = await bot.MakeAsync(owner, "Finals");
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		var (succeeded, said) = await bot.TypeAsync(typist, "#osu", $"!mp in {room.Id}");

		Assert.False(succeeded);
		Assert.Equal([bot.Text(MpReplies.MpInDmOnly)], said.Here);
		Assert.Null(bot.Targets.Get(typist.Record.Id));
	}

	[Fact]
	public async Task In_WithoutAnId_SaysNothingIsTargetedAndForgetsTheTargetedRoom()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");
		await bot.MakeAsync(typist, "Finals");
		Assert.NotNull(bot.Targets.Get(typist.Record.Id));

		var (succeeded, said) = await bot.TypeAsync(typist, null, "!mp in");

		Assert.True(succeeded);
		Assert.Equal([bot.Text(MpReplies.NotScopedToAnyMatch)], said.Here);
		Assert.Null(bot.Targets.Get(typist.Record.Id));
	}

	[Fact]
	public async Task In_WithANonNumericId_SaysHowToUseItAndFails()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		var (succeeded, said) = await bot.TypeAsync(typist, null, "!mp in x");

		Assert.False(succeeded);
		Assert.Equal([bot.Text(MpReplies.InUsage)], said.Here);
	}

	[Fact]
	public async Task In_ForAnIdNoRoomHas_SaysThereIsNoActiveMatch()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		var (succeeded, said) = await bot.TypeAsync(typist, null, "!mp in 99");

		Assert.False(succeeded);
		Assert.Equal([bot.Text(MpReplies.NoActiveRoomWithHashId, new { room = 99 })], said.Here);
	}

	[Fact]
	public async Task In_ForAVisibleRoom_SaysTheRoomIsTargetedWithoutTheRoomPrefix()
	{
		using var bot = new BotHarness();
		var owner = await RoomFixture.PlayerAsync(bot.App, "Owner");
		var room = await bot.MakeAsync(owner, "Finals");
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		var (succeeded, said) = await bot.TypeAsync(typist, null, $"!mp in {room.Id}");

		Assert.True(succeeded);
		Assert.Equal([bot.Text(MpReplies.NowTargetingMatch, new { room = room.Id, name = "Finals" })], said.Here);
	}

	[Fact]
	public async Task In_ForAVisibleRoom_MakesTheTypistTargetIt()
	{
		using var bot = new BotHarness();
		var owner = await RoomFixture.PlayerAsync(bot.App, "Owner");
		var room = await bot.MakeAsync(owner, "Finals");
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		await bot.TypeAsync(typist, null, $"!mp in {room.Id}");

		Assert.Same(room, bot.Targets.Get(typist.Record.Id));
	}

	[Fact]
	public async Task Close_ByTheCreator_SaysTheMatchWasClosedAndClosesIt()
	{
		using var bot = new BotHarness();
		var creator = await RoomFixture.PlayerAsync(bot.App, "Creator");
		var room = await bot.MakeAsync(creator, "Finals");

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp close");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.ClosedMatch)], said.Here);
		Assert.True(room.IsClosed);
	}

	[Fact]
	public async Task Close_ByASeatedPlayerWhoIsNoReferee_IsRefusedAndTheRoomStaysOpen()
	{
		using var bot = new BotHarness();
		var creator = await RoomFixture.PlayerAsync(bot.App, "Creator");
		var room = await bot.MakeAsync(creator, "Finals");
		var player = await RoomFixture.PlayerAsync(bot.App, "Player");
		Assert.True((await bot.TypeAsync(player, "#osu", $"!mp join {room.Id}")).Succeeded);

		var (succeeded, said) = await bot.TypeAsync(player, "#osu", "!mp close");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.NotAuthorizedInMatch)], said.Here);
		Assert.False(room.IsClosed);
		Assert.Contains(room, bot.App.Lobby.Rooms.Values);
	}
}