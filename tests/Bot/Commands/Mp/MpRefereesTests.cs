using Basil.Application.Users.Objects.Connections;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Bot.Commands.Mp;
using Basil.Bot.Tests.Support;
using Basil.Testing;

namespace Basil.Bot.Tests.Commands.Mp;

/// <summary>
///     The commands that manage and list the referees and the bans of a room: <c>!mp addref</c>, <c>removeref</c>,
///     <c>listrefs</c> and <c>banlist</c>. Expected replies are named by the reply that owns the wording.
/// </summary>
public sealed class MpRefereesTests
{
	/// <summary>The most referees a match can have, besides its creator.</summary>
	private const int RefereeLimit = 8;

	[Fact]
	public async Task AddRef_WithoutAName_SaysHowToUseIt()
	{
		using var bot = new BotHarness();
		var (room, creator) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp addref");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.AddRefUsage)], said.Here);
		Assert.Empty(room.Authority.Referees);
	}

	[Fact]
	public async Task AddRef_ForANameNoAccountHas_SaysTheUserWasNotFound()
	{
		using var bot = new BotHarness();
		var (room, creator) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp addref Nobody");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.UserNotFound)], said.Here);
		Assert.Empty(room.Authority.Referees);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task AddRef_ForARegisteredUser_SaysSoAndMakesThemAReferee(bool online)
	{
		using var bot = new BotHarness();
		var (room, creator) = await SetUpAsync(bot);
		var alice = online
			? (await RoomFixture.PlayerAsync(bot.App, "Alice")).Record
			: await bot.App.CreateUserAsync("Alice", "password");

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp addref Alice");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.AddedReferee, new { player = "Alice" })], said.Here);
		Assert.Contains(alice, room.Authority.Referees);
	}

	[Fact]
	public async Task AddRef_ForAUserWhoIsAlreadyAReferee_SaysSoAndKeepsOneEntry()
	{
		using var bot = new BotHarness();
		var (room, creator) = await SetUpAsync(bot);
		await bot.App.CreateUserAsync("Alice", "password");
		Assert.True((await bot.TypeAsync(creator, "#osu", "!mp addref Alice")).Succeeded);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp addref Alice");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.TargetAlreadyReferee)], said.Here);
		Assert.Single(room.Authority.Referees);
	}

	[Fact]
	public async Task AddRef_ForTheCreator_SaysTheUserCreatedTheMatchAndAddsNobody()
	{
		using var bot = new BotHarness();
		var (room, creator) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp addref Creator");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.TargetIsCreator)], said.Here);
		Assert.Empty(room.Authority.Referees);
	}

	[Fact]
	public async Task AddRef_WhenTheMatchHasTheMostRefereesItCanHave_SaysTheLimitWasReachedAndAddsNobody()
	{
		using var bot = new BotHarness();
		var (room, creator) = await SetUpAsync(bot);
		for (var number = 1; number <= RefereeLimit; number++)
		{
			await bot.App.CreateUserAsync($"Ref{number}", "password");
			Assert.True((await bot.TypeAsync(creator, "#osu", $"!mp addref Ref{number}")).Succeeded);
		}

		var extra = await bot.App.CreateUserAsync("Extra", "password");

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp addref Extra");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.RefereeLimitReached)], said.Here);
		Assert.Equal(RefereeLimit, room.Authority.Referees.Count);
		Assert.DoesNotContain(extra, room.Authority.Referees);
	}

	[Fact]
	public async Task RemoveRef_WithoutAName_SaysHowToUseIt()
	{
		using var bot = new BotHarness();
		var (room, creator) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp removeref");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.RemoveRefUsage)], said.Here);
	}

	[Fact]
	public async Task RemoveRef_OfAReferee_SaysSoAndTakesThemOffTheRefereeList()
	{
		using var bot = new BotHarness();
		var (room, creator) = await SetUpAsync(bot);
		var alice = await bot.App.CreateUserAsync("Alice", "password");
		Assert.True((await bot.TypeAsync(creator, "#osu", "!mp addref Alice")).Succeeded);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp removeref Alice");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.RemovedReferee, new { player = "Alice" })], said.Here);
		Assert.DoesNotContain(alice, room.Authority.Referees);
	}

	[Fact]
	public async Task RemoveRef_OfAUserWhoIsNoReferee_SaysThatUserIsNotAReferee()
	{
		using var bot = new BotHarness();
		var (room, creator) = await SetUpAsync(bot);
		await bot.App.CreateUserAsync("Alice", "password");

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp removeref Alice");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.TargetNotReferee)], said.Here);
	}

	[Theory]
	[InlineData("!mp addref Third")]
	[InlineData("!mp removeref Other")]
	public async Task RefereeCommand_ByARefereeWhoIsNotTheCreator_IsRefusedAndChangesNoReferee(string message)
	{
		using var bot = new BotHarness();
		var (room, creator) = await SetUpAsync(bot);
		var referee = await RoomFixture.PlayerAsync(bot.App, "Referee");
		await bot.App.CreateUserAsync("Other", "password");
		await bot.App.CreateUserAsync("Third", "password");
		Assert.True((await bot.TypeAsync(creator, "#osu", "!mp addref Referee")).Succeeded);
		Assert.True((await bot.TypeAsync(creator, "#osu", "!mp addref Other")).Succeeded);
		Assert.True((await bot.TypeAsync(referee, null, $"!mp in {room.Id}")).Succeeded);
		var before = RefereeIds(room);

		var (succeeded, said) = await bot.TypeAsync(referee, "#osu", message);

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.NotAuthorizedInMatch)], said.Here);
		Assert.Equal(before, RefereeIds(room));
	}

	[Fact]
	public async Task ListRefs_WithNoReferees_SaysThereAreNone()
	{
		using var bot = new BotHarness();
		var (room, creator) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp listrefs");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.NoReferees)], said.Here);
	}

	[Fact]
	public async Task ListRefs_WithReferees_SaysTheHeaderThenOneLinePerRefereeWithTheRoomPrefix()
	{
		using var bot = new BotHarness();
		var (room, creator) = await SetUpAsync(bot);
		var alice = await bot.App.CreateUserAsync("Alice", "password");
		var bob = await bot.App.CreateUserAsync("Bob", "password");
		Assert.True((await bot.TypeAsync(creator, "#osu", "!mp addref Alice")).Succeeded);
		Assert.True((await bot.TypeAsync(creator, "#osu", "!mp addref Bob")).Succeeded);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp listrefs");

		Assert.True(succeeded);
		var lines = Assert.Single(said.Here).Split('\n');
		Assert.Equal(bot.Prefixed(room.Id, bot.Text(MpReplies.MatchReferees)), lines[0]);
		Assert.Equal(
			new[] { bot.Prefixed(room.Id, $"#{alice.Id} Alice"), bot.Prefixed(room.Id, $"#{bob.Id} Bob") }.Order(),
			lines.Skip(1).Order());
	}

	[Fact]
	public async Task BanList_WithNoBans_SaysThereAreNone()
	{
		using var bot = new BotHarness();
		var (room, creator) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp banlist");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.NoBannedPlayers)], said.Here);
	}

	[Fact]
	public async Task BanList_WithBans_SaysTheHeaderThenOneLinePerBannedUserWithTheRoomPrefix()
	{
		using var bot = new BotHarness();
		var (room, creator) = await SetUpAsync(bot);
		var alice = await bot.App.CreateUserAsync("Alice", "password");
		var bob = await bot.App.CreateUserAsync("Bob", "password");
		Assert.True((await bot.TypeAsync(creator, "#osu", "!mp ban Alice")).Succeeded);
		Assert.True((await bot.TypeAsync(creator, "#osu", "!mp ban Bob")).Succeeded);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp banlist");

		Assert.True(succeeded);
		var lines = Assert.Single(said.Here).Split('\n');
		Assert.Equal(bot.Prefixed(room.Id, bot.Text(MpReplies.MatchBans)), lines[0]);
		Assert.Equal(
			new[] { bot.Prefixed(room.Id, $"#{alice.Id} Alice"), bot.Prefixed(room.Id, $"#{bob.Id} Bob") }.Order(),
			lines.Skip(1).Order());
	}

	/// <summary>Opens a room as "Creator" with <c>!mp make</c>; the creator targets it.</summary>
	private static async Task<(Room Room, BanchoConnection Creator)> SetUpAsync(BotHarness bot)
	{
		var creator = await RoomFixture.PlayerAsync(bot.App, "Creator");
		var room = await bot.MakeAsync(creator, "Finals");
		return (room, creator);
	}

	/// <summary>Lists the ids of the referees of a room, lowest first.</summary>
	private static int[] RefereeIds(Room room)
	{
		return [.. room.Authority.Referees.Select(referee => referee.Id).Order()];
	}
}