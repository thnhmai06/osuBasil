using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects.Connections;
using Basil.Bot.Commands.Mp;
using Basil.Bot.Tests.Support;
using Basil.Testing;

namespace Basil.Bot.Tests.Commands.Mp;

/// <summary>
///     The commands that start and stop rounds and countdowns: <c>!mp start</c>, <c>timer</c>, <c>aborttimer</c> and
///     <c>abort</c>. Expected replies are named by the reply that owns the wording; the countdown announcements belong
///     to the room, so the commands themselves say nothing when a countdown starts.
/// </summary>
public sealed class MpRoundsTests
{
	[Fact]
	public async Task Start_WithoutABeatmap_SaysNoBeatmapIsSelectedAndStartsNoRound()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp start");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.NoBeatmapSelected)], said.Here);
		Assert.False(room.Rounds.InProgress);
		Assert.Null(room.Rounds.LastRound);
	}

	[Fact]
	public async Task Abort_WithNoRoundInProgress_SaysTheMatchIsNotInProgress()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp abort");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.MatchNotInProgress)], said.Here);
	}

	[Fact]
	public async Task AbortTimer_WithNoCountdown_SaysNoCountdownIsRunning()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp aborttimer");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.NoCountdownRunning)], said.Here);
	}

	[Fact]
	public async Task Timer_WithoutSeconds_StartsACountdownOf30SecondsThatDoesNotStartTheRound()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);

		await bot.TypeAsync(creator, "#osu", "!mp timer");

		var countdown = Assert.IsType<RoomCountdown>(room.Rounds.Countdown);
		Assert.Equal(TimeSpan.FromSeconds(30), countdown.Length);
		Assert.False(countdown.StartsRound);
		Assert.False(room.Rounds.InProgress);
	}

	[Fact]
	public async Task Timer_WithoutSeconds_SucceedsAndSaysNothingBecauseTheRoomAnnounces()
	{
		using var bot = new BotHarness();
		var (_, creator, _) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp timer");

		Assert.True(succeeded);
		Assert.Empty(said.All);
	}

	[Fact]
	public async Task Timer_With90_StartsACountdownOf90Seconds()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);

		await bot.TypeAsync(creator, "#osu", "!mp timer 90");

		var countdown = Assert.IsType<RoomCountdown>(room.Rounds.Countdown);
		Assert.Equal(TimeSpan.FromSeconds(90), countdown.Length);
		Assert.False(countdown.StartsRound);
	}

	[Theory]
	[InlineData("0")]
	[InlineData("-1")]
	[InlineData("x")]
	public async Task Timer_WithZeroANegativeNumberOrNoNumber_SaysHowToUseItAndStartsNoCountdown(string seconds)
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", $"!mp timer {seconds}");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.TimerUsage)], said.Here);
		Assert.Null(room.Rounds.Countdown);
	}

	[Fact]
	public async Task Timer_WithOneHour_StartsTheCountdown()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp timer 3600");

		Assert.True(succeeded);
		Assert.Empty(said.All);
		Assert.Equal(TimeSpan.FromHours(1), room.Rounds.Countdown!.Length);
	}

	[Fact]
	public async Task Timer_WithMoreThanOneHour_SaysTheCountdownIsOutOfRangeAndStartsNone()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp timer 3601");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.CountdownOutOfRange)], said.Here);
		Assert.Null(room.Rounds.Countdown);
	}

	[Fact]
	public async Task AbortTimer_AfterATimer_SaysTheCountdownWasAbortedAndLeavesNoCountdown()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);
		Assert.True((await bot.TypeAsync(creator, "#osu", "!mp timer 60")).Succeeded);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp aborttimer");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.CountdownAborted)], said.Here);
		Assert.Null(room.Rounds.Countdown);
	}

	[Fact]
	public async Task Start_WithSecondsAndABeatmap_StartsACountdownThatStartsTheRoundAndSaysNothing()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot, true);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp start 30");

		Assert.True(succeeded);
		Assert.Empty(said.All);
		var countdown = Assert.IsType<RoomCountdown>(room.Rounds.Countdown);
		Assert.Equal(TimeSpan.FromSeconds(30), countdown.Length);
		Assert.True(countdown.StartsRound);
		Assert.False(room.Rounds.InProgress);
	}

	[Fact]
	public async Task Start_WithSeconds_DoesNotStartTheRoundBeforeTheCountdownElapses()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot, true);
		Assert.True((await bot.TypeAsync(creator, "#osu", "!mp start 30")).Succeeded);

		bot.App.Clock.Advance(TimeSpan.FromSeconds(29));

		Assert.False(room.Rounds.InProgress);
	}

	[Fact]
	public async Task Start_WithSeconds_StartsTheRoundWhenTheCountdownElapses()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot, true);
		Assert.True((await bot.TypeAsync(creator, "#osu", "!mp start 30")).Succeeded);

		bot.App.Clock.Advance(TimeSpan.FromSeconds(30));

		Assert.True(room.Rounds.InProgress);
		Assert.Null(room.Rounds.Countdown);
	}

	[Fact]
	public async Task Start_WithoutSecondsWithABeatmapAndSeatedPlayers_SaysTheMatchStartedAndStartsTheRoundAtOnce()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot, true);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp start");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.MatchStarted)], said.Here);
		Assert.True(room.Rounds.InProgress);
	}

	[Fact]
	public async Task Abort_DuringARound_SaysTheMatchWasAbortedAndEndsTheRound()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot, true);
		Assert.True((await bot.TypeAsync(creator, "#osu", "!mp start")).Succeeded);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp abort");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.AbortedMatch)], said.Here);
		Assert.False(room.Rounds.InProgress);
	}

	[Theory]
	[InlineData("!mp start")]
	[InlineData("!mp start 30")]
	[InlineData("!mp timer")]
	[InlineData("!mp timer 90")]
	[InlineData("!mp aborttimer")]
	[InlineData("!mp abort")]
	public async Task RoundCommand_ByAPlainSeatedPlayer_IsRefusedAndLeavesTheCountdownAndTheRoundAlone(string message)
	{
		using var bot = new BotHarness();
		var (room, creator, player) = await SetUpAsync(bot, true);
		Assert.True((await bot.TypeAsync(creator, "#osu", "!mp timer 60")).Succeeded);
		var countdown = room.Rounds.Countdown;

		var (succeeded, said) = await bot.TypeAsync(player, "#osu", message);

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.NotAuthorizedInMatch)], said.Here);
		Assert.Same(countdown, room.Rounds.Countdown);
		Assert.Null(room.Rounds.LastRound);
	}

	/// <summary>
	///     Opens a room as "Creator" (seated in slot 1 as host, targeting the room) and seats "Player" with
	///     <c>!mp join</c>; a beatmap is selected on request through the room, as the creator.
	/// </summary>
	private static async Task<(Room Room, BanchoConnection Creator, BanchoConnection Player)> SetUpAsync(BotHarness bot,
		bool withBeatmap = false)
	{
		var creator = await RoomFixture.PlayerAsync(bot.App, "Creator");
		var room = await bot.MakeAsync(creator, "Finals");
		var player = await RoomFixture.PlayerAsync(bot.App, "Player");
		Assert.True((await bot.TypeAsync(player, "#osu", $"!mp join {room.Id}")).Succeeded);
		if (withBeatmap)
			Assert.True((await room.ScopeTo(creator).Settings.ConfigureAsync(
				new RoomSettingsChange(Beatmap: RoomFixture.Beatmap), TestContext.Current.CancellationToken)).IsOk);
		return (room, creator, player);
	}
}