using Basil.Application.Bot.Commands.Mp;
using Basil.Domain.Users;
using Basil.Infrastructure.Bot.Tests.Support;
using Basil.Testing;

namespace Basil.Infrastructure.Bot.Tests.Commands.Mp;

/// <summary>
///     The commands that open, enter, target and close rooms: <c>!mp make</c>, <c>!mp makeprivate</c>, <c>!mp join</c>,
///     <c>!mp in</c> and <c>!mp close</c>. Expected replies are named by the reply that owns the wording.
/// </summary>
public sealed class MpLifecycleTests
{
	[Fact]
	public async Task UserWithClient_SaysCreatedPublic()
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
	public async Task UserWithClient_OpensPublicRoom()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		await bot.TypeAsync(typist, "#osu", "!mp make Finals");

		var room = Assert.Single(bot.App.Lobby.Rooms.Values);
		Assert.Equal(typist.Record, room.Authority.Creator);
		Assert.False(room.Settings.IsPrivate);
		Assert.Equal("Finals", room.Settings.Name);
	}

	[Fact]
	public async Task OnlineNoRoom_SeatsAsHost()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		await bot.TypeAsync(typist, "#osu", "!mp make Finals");

		var room = Assert.Single(bot.App.Lobby.Rooms.Values);
		Assert.NotNull(room.Slots.Find(typist));
		Assert.Same(typist, room.Authority.Host);
	}

	[Fact]
	public async Task Make_TypistTargetsRoom()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		await bot.TypeAsync(typist, "#osu", "!mp make Finals");

		var room = Assert.Single(bot.App.Lobby.Rooms.Values);
		Assert.Same(room, bot.Targets.Get(typist.Record.Id));
	}

	[Fact]
	public async Task MakePrivate_SaysCreatedPrivate()
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
	public async Task MakePrivate_OpensPrivateRoom()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		await bot.TypeAsync(typist, "#osu", "!mp makeprivate Finals");

		Assert.True(Assert.Single(bot.App.Lobby.Rooms.Values).Settings.IsPrivate);
	}

	[Fact]
	public async Task WithoutName_NamedAfterTypist()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		await bot.TypeAsync(typist, "#osu", "!mp make");

		var room = Assert.Single(bot.App.Lobby.Rooms.Values);
		Assert.Equal(bot.Text(MpReplies.DefaultRoomName, new { name = "Typist" }), room.Settings.Name);
	}

	[Fact]
	public async Task LongName_KeepsFirst50()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");
		var name = string.Concat(Enumerable.Repeat("abcdefghij", 6));

		await bot.TypeAsync(typist, "#osu", $"!mp make {name}");

		var room = Assert.Single(bot.App.Lobby.Rooms.Values);
		Assert.Equal(name[..50], room.Settings.Name);
	}

	[Fact]
	public async Task NoPlayerPrivilege_MissingNoOpen()
	{
		using var bot = new BotHarness();
		var typist =
			await RoomFixture.PlayerAsync(bot.App, "Typist",
				UserPrivilegesExtensions.Default & ~UserPrivileges.Player);

		var (succeeded, said) = await bot.TypeAsync(typist, "#osu", "!mp make Finals");

		Assert.False(succeeded);
		Assert.Equal([bot.Text(MpReplies.MissingPrivileges)], said.Here);
		Assert.Empty(bot.App.Lobby.Rooms);
	}

	[Theory]
	[InlineData("!mp join")]
	[InlineData("!mp join abc")]
	public async Task NoNumericId_UsageAndFails(string message)
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		var (succeeded, said) = await bot.TypeAsync(typist, "#osu", message);

		Assert.False(succeeded);
		Assert.Equal([bot.Text(MpReplies.JoinUsage)], said.Here);
	}

	[Fact]
	public async Task IdNoRoom_NoActiveMatch()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		var (succeeded, said) = await bot.TypeAsync(typist, "#osu", "!mp join 99");

		Assert.False(succeeded);
		Assert.Equal([bot.Text(MpReplies.NoActiveRoomWithId, new { room = 99 })], said.Here);
	}

	[Fact]
	public async Task PublicRoom_SaysTypistJoined()
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
	public async Task PublicRoom_SeatsTypist()
	{
		using var bot = new BotHarness();
		var owner = await RoomFixture.PlayerAsync(bot.App, "Owner");
		var room = await bot.MakeAsync(owner, "Finals");
		var joiner = await RoomFixture.PlayerAsync(bot.App, "Joiner");

		await bot.TypeAsync(joiner, "#osu", $"!mp join {room.Id}");

		Assert.NotNull(room.Slots.Find(joiner.Record));
	}

	[Fact]
	public async Task PublicRoom_TypistTargetsIt()
	{
		using var bot = new BotHarness();
		var owner = await RoomFixture.PlayerAsync(bot.App, "Owner");
		var room = await bot.MakeAsync(owner, "Finals");
		var joiner = await RoomFixture.PlayerAsync(bot.App, "Joiner");

		await bot.TypeAsync(joiner, "#osu", $"!mp join {room.Id}");

		Assert.Same(room, bot.Targets.Get(joiner.Record.Id));
	}

	[Fact]
	public async Task RoomPassword_SeatsTypist()
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
	public async Task WrongPassword_IncorrectNoSeat(string suffix)
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
	public async Task StrangerPrivateRoom_JoinsAnyway()
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
	public async Task TypedInChannel_OnlyPrivateFails()
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
	public async Task NoId_UntargetsAndForgets()
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
	public async Task NonNumericId_UsageAndFails()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		var (succeeded, said) = await bot.TypeAsync(typist, null, "!mp in x");

		Assert.False(succeeded);
		Assert.Equal([bot.Text(MpReplies.InUsage)], said.Here);
	}

	[Fact]
	public async Task UnknownId_NoActiveMatch()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		var (succeeded, said) = await bot.TypeAsync(typist, null, "!mp in 99");

		Assert.False(succeeded);
		Assert.Equal([bot.Text(MpReplies.NoActiveRoomWithHashId, new { room = 99 })], said.Here);
	}

	[Fact]
	public async Task VisibleRoom_TargetedWithoutPrefix()
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
	public async Task VisibleRoom_TypistTargetsIt()
	{
		using var bot = new BotHarness();
		var owner = await RoomFixture.PlayerAsync(bot.App, "Owner");
		var room = await bot.MakeAsync(owner, "Finals");
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		await bot.TypeAsync(typist, null, $"!mp in {room.Id}");

		Assert.Same(room, bot.Targets.Get(typist.Record.Id));
	}

	[Fact]
	public async Task ByCreator_SaidClosed()
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
	public async Task SeatedNonReferee_RefusedStaysOpen()
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