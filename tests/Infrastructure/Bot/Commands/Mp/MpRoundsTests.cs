using Basil.Application.Bot.Announcements;
using Basil.Application.Bot.Commands.Mp;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects.Connections;
using Basil.Infrastructure.Bot.Tests.Support;
using Basil.Infrastructure.Bot.Workers;
using Basil.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Bot.Tests.Commands.Mp;

/// <summary>
///     The commands that start and stop rounds and countdowns: <c>!mp start</c>, <c>timer</c>, <c>aborttimer</c> and
///     <c>abort</c>. Expected replies are named by the reply that owns the wording; the countdown announces itself in
///     the room, so the commands themselves say nothing when a countdown starts.
/// </summary>
public sealed class MpRoundsTests
{
	[Fact]
	public async Task NoBeatmap_NoRoundStart()
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
	public async Task NoRoomRunning_NotInProgress()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp abort");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.MatchNotInProgress)], said.Here);
	}

	[Fact]
	public async Task NoCountdown_SaysNotRunning()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp aborttimer");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.NoCountdownRunning)], said.Here);
	}

	[Fact]
	public async Task NoSeconds_Countdown30NoRound()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);

		await bot.TypeAsync(creator, "#osu", "!mp timer");

		Assert.Equal([bot.Text(AnnouncementReplies.CountdownStarted, new { seconds = 30 })], SaidInRoom(bot, room));
		bot.App.Clock.Advance(TimeSpan.FromSeconds(30));
		Assert.Equal(bot.Text(AnnouncementReplies.CountdownElapsed), SaidInRoom(bot, room)[^1]);
		Assert.False(room.Rounds.InProgress);
	}

	[Fact]
	public async Task NoSeconds_SucceedsSilently()
	{
		using var bot = new BotHarness();
		var (_, creator, _) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp timer");

		Assert.True(succeeded);
		Assert.Empty(said.All);
	}

	[Fact]
	public async Task With90_Starts90Countdown()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);

		await bot.TypeAsync(creator, "#osu", "!mp timer 90");

		Assert.Equal([bot.Text(AnnouncementReplies.CountdownStarted, new { seconds = 90 })], SaidInRoom(bot, room));
	}

	[Theory]
	[InlineData("0")]
	[InlineData("-1")]
	[InlineData("x")]
	public async Task InvalidSeconds_UsageNoCountdown(string seconds)
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", $"!mp timer {seconds}");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.TimerUsage)], said.Here);
		Assert.Empty(SaidInRoom(bot, room));
		Assert.False(IsCountdownRunning(bot, room));
	}

	[Fact]
	public async Task WithOneHour_StartsTheCountdown()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp timer 3600");

		Assert.True(succeeded);
		Assert.Empty(said.All);
		Assert.Equal(bot.Text(AnnouncementReplies.CountdownStarted, new { seconds = 3600 }), SaidInRoom(bot, room)[0]);
	}

	[Fact]
	public async Task OverAnHour_RangeErrorNone()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp timer 3601");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.CountdownOutOfRange)], said.Here);
		Assert.False(IsCountdownRunning(bot, room));
	}

	[Fact]
	public async Task AfterTimer_AbortAnnouncedOnce()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);
		Assert.True((await bot.TypeAsync(creator, "#osu", "!mp timer 60")).Succeeded);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp aborttimer");

		Assert.True(succeeded);
		Assert.Empty(said.Here);
		Assert.False(IsCountdownRunning(bot, room));
		Assert.Single(SaidInRoom(bot, room), bot.Text(AnnouncementReplies.CountdownCancelled));
	}

	[Fact]
	public async Task SecondsAndBeatmap_CountdownThenRound()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot, true);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp start 30");

		Assert.True(succeeded);
		Assert.Empty(said.All);
		Assert.Equal([bot.Text(AnnouncementReplies.CountdownStartedForRound, new { seconds = 30 })],
			SaidInRoom(bot, room));
		Assert.False(room.Rounds.InProgress);
	}

	[Fact]
	public async Task WithSeconds_RoundNotYet()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot, true);
		Assert.True((await bot.TypeAsync(creator, "#osu", "!mp start 30")).Succeeded);

		bot.App.Clock.Advance(TimeSpan.FromSeconds(29));

		Assert.False(room.Rounds.InProgress);
	}

	[Fact]
	public async Task WithSeconds_RoundOnElapse()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot, true);
		Assert.True((await bot.TypeAsync(creator, "#osu", "!mp start 30")).Succeeded);

		bot.App.Clock.Advance(TimeSpan.FromSeconds(30));

		Assert.True(room.Rounds.InProgress);
		Assert.False(IsCountdownRunning(bot, room));
	}

	[Fact]
	public async Task NoSeconds_RoundAtOnce()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot, true);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp start");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.MatchStarted)], said.Here);
		Assert.True(room.Rounds.InProgress);
	}

	[Fact]
	public async Task DuringRound_AbortedAndEnds()
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
	public async Task PlainSeatedPlayer_RefusedLeavesAlone(string message)
	{
		using var bot = new BotHarness();
		var (room, creator, player) = await SetUpAsync(bot, true);
		Assert.True((await bot.TypeAsync(creator, "#osu", "!mp timer 60")).Succeeded);

		var (succeeded, said) = await bot.TypeAsync(player, "#osu", message);

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.NotAuthorizedInMatch)], said.Here);
		Assert.True(IsCountdownRunning(bot, room));
		Assert.Null(room.Rounds.LastRound);
	}

	private static List<string> SaidInRoom(BotHarness bot, Room room)
	{
		return bot.SaidIn(bot.App.RoomChannels.Of(room));
	}

	private static bool IsCountdownRunning(BotHarness bot, Room room)
	{
		return bot.App.Provider.GetRequiredService<RoomCountdowns>().IsRunning(room);
	}

	/// <summary>
	///     Opens a room as "Creator" (seated in slot 1 as host, targeting the room) and seats "Player" with
	///     <c>!mp join</c>; a beatmap is selected on request through the room, as the creator.
	/// </summary>
	private static async Task<(Room Room, StableConnection Creator, StableConnection Player)> SetUpAsync(
		BotHarness bot,
		bool withBeatmap = false)
	{
		await bot.StartBotAsync();
		CountdownEvents.Deliver(bot);
		var creator = await RoomFixture.PlayerAsync(bot.App, "Creator");
		var room = await bot.MakeAsync(creator, "Finals");
		var player = await RoomFixture.PlayerAsync(bot.App, "Player");
		Assert.True((await bot.TypeAsync(player, "#osu", $"!mp join {room.Id}")).Succeeded);
		if (withBeatmap)
			Assert.True((await room.ScopeTo(creator).Settings.ConfigureAsync(
				new RoomSettingsChange(Beatmap: RoomFixture.Beatmap))) == RoomOutcome.Ok);
		return (room, creator, player);
	}
}