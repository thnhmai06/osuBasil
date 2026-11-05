using Basil.Application.Bot.Announcements;
using Basil.Application.Bot.Registry;
using Basil.Application.Common;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Authentication;
using Basil.Domain.Mechanics;
using Basil.Infrastructure.Bot.Tests.Support;
using Basil.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Bot.Tests.Countdowns;

/// <summary>
///     The countdowns BasilBot runs in rooms, driven through the messages the hosts send. The expected behaviour comes
///     from docs/for-developers/multiplayer.md ("Countdown"): a countdown announces its marks in the room, starts the
///     round it was for when it elapses, and is cancelled by whatever makes it pointless.
/// </summary>
public sealed class RoomCountdownsTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Start_ForATimer_AnnouncesTheSixtyThirtyTenAndFiveSecondMarksAndThenTheEnd()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		Assert.True((await StartAsync(bot, staff.Creator, staff.Room, TimeSpan.FromMinutes(5), false)).IsOk);
		bot.App.Clock.Advance(TimeSpan.FromSeconds(240));
		bot.App.Clock.Advance(TimeSpan.FromSeconds(30));
		bot.App.Clock.Advance(TimeSpan.FromSeconds(20));
		bot.App.Clock.Advance(TimeSpan.FromSeconds(5));
		bot.App.Clock.Advance(TimeSpan.FromSeconds(5));

		Assert.Equal(
		[
			bot.Text(AnnouncementReplies.CountdownStarted, new { seconds = 300 }),
			bot.Text(AnnouncementReplies.CountdownTicked, new { seconds = 60 }),
			bot.Text(AnnouncementReplies.CountdownTicked, new { seconds = 30 }),
			bot.Text(AnnouncementReplies.CountdownTicked, new { seconds = 10 }),
			bot.Text(AnnouncementReplies.CountdownTicked, new { seconds = 5 }),
			bot.Text(AnnouncementReplies.CountdownElapsed)
		], SaidInRoom(bot, staff.Room));
		Assert.False(staff.Room.Rounds.InProgress);
		Assert.False(IsRunning(bot, staff.Room));
	}

	[Fact]
	public async Task Start_ForARound_AnnouncesTheMarksAndStartsTheRoundWhenItElapses()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot, true);

		Assert.True((await StartAsync(bot, staff.Referee, staff.Room, TimeSpan.FromSeconds(90), true)).IsOk);
		bot.App.Clock.Advance(TimeSpan.FromSeconds(90));

		var forRound = (int seconds) => bot.Text(AnnouncementReplies.CountdownTickedForRound, new { seconds });
		Assert.Equal(
		[
			bot.Text(AnnouncementReplies.CountdownStartedForRound, new { seconds = 90 }),
			forRound(60), forRound(30), forRound(10), forRound(5), forRound(4), forRound(3),
			bot.Text(AnnouncementReplies.RoundStartedByCountdown)
		], SaidInRoom(bot, staff.Room));
		Assert.True(staff.Room.Rounds.InProgress);
		Assert.False(IsRunning(bot, staff.Room));
	}

	[Fact]
	public async Task Start_ForARound_DoesNotStartTheRoundBeforeItElapses()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot, true);
		Assert.True((await StartAsync(bot, staff.Creator, staff.Room, TimeSpan.FromSeconds(30), true)).IsOk);

		bot.App.Clock.Advance(TimeSpan.FromSeconds(29));

		Assert.False(staff.Room.Rounds.InProgress);
		Assert.True(IsRunning(bot, staff.Room));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Start_ByAPlainPlayer_IsRefusedAndStartsNothing(bool startsRound)
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot, true);

		var result = await StartAsync(bot, staff.Player, staff.Room, TimeSpan.FromMinutes(5), startsRound);
		bot.App.Clock.Advance(TimeSpan.FromHours(2));

		Assert.Equal(RoomOutcome.RoleDenied, result.Outcome);
		Assert.Empty(SaidInRoom(bot, staff.Room));
		Assert.False(staff.Room.Rounds.InProgress);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(999)]
	[InlineData(3_600_001)]
	public async Task Start_WithALengthOutsideOneSecondToOneHour_IsRefused(int milliseconds)
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		var result = await StartAsync(bot, staff.Creator, staff.Room, TimeSpan.FromMilliseconds(milliseconds), false);

		Assert.Equal(RoomOutcome.InvalidSettings, result.Outcome);
		Assert.False(IsRunning(bot, staff.Room));
	}

	[Theory]
	[InlineData(1_000)]
	[InlineData(3_600_000)]
	public async Task Start_WithAnAcceptedBoundaryLength_Starts(int milliseconds)
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		var result = await StartAsync(bot, staff.Creator, staff.Room, TimeSpan.FromMilliseconds(milliseconds), false);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.True(IsRunning(bot, staff.Room));
	}

	[Fact]
	public async Task Start_ForARoundWhileARoundIsInProgress_IsRefused()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot, true);
		Assert.True((await staff.Room.ScopeTo(staff.Creator).Rounds.StartAsync(Token)).IsOk);

		var result = await StartAsync(bot, staff.Creator, staff.Room, TimeSpan.FromMinutes(1), true);

		Assert.Equal(RoomOutcome.InProgress, result.Outcome);
		Assert.False(IsRunning(bot, staff.Room));
	}

	[Fact]
	public async Task Start_ForATimerWhileARoundIsInProgress_Starts()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot, true);
		Assert.True((await staff.Room.ScopeTo(staff.Creator).Rounds.StartAsync(Token)).IsOk);

		var result = await StartAsync(bot, staff.Creator, staff.Room, TimeSpan.FromMinutes(1), false);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.True(IsRunning(bot, staff.Room));
	}

	[Fact]
	public async Task Start_InAClosedRoom_IsRefused()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);
		Assert.True((await staff.Room.ScopeTo(staff.Creator).CloseAsync(Token)).IsOk);

		var result = await StartAsync(bot, staff.Creator, staff.Room, TimeSpan.FromMinutes(1), false);

		Assert.Equal(RoomOutcome.RoomClosed, result.Outcome);
	}

	[Fact]
	public async Task Start_WhileOneRuns_ReplacesItAndOnlyTheNewOneElapses()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);
		Assert.True((await StartAsync(bot, staff.Creator, staff.Room, TimeSpan.FromSeconds(60), false)).IsOk);
		bot.App.Clock.Advance(TimeSpan.FromSeconds(15));

		Assert.True((await StartAsync(bot, staff.Creator, staff.Room, TimeSpan.FromSeconds(30), false)).IsOk);
		bot.App.Clock.Advance(TimeSpan.FromSeconds(60));

		var said = SaidInRoom(bot, staff.Room);
		Assert.Equal(bot.Text(AnnouncementReplies.CountdownStarted, new { seconds = 60 }), said[0]);
		Assert.Equal(bot.Text(AnnouncementReplies.CountdownReplaced, new { seconds = 45 }), said[1]);
		Assert.Equal(bot.Text(AnnouncementReplies.CountdownStarted, new { seconds = 30 }), said[2]);
		Assert.Single(said, bot.Text(AnnouncementReplies.CountdownElapsed));
	}

	[Fact]
	public async Task Elapse_WhenTheStartingConnectionIsGone_CancelsTheCountdownAndStartsNoRound()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot, true);
		Assert.True((await StartAsync(bot, staff.Referee, staff.Room, TimeSpan.FromSeconds(10), true)).IsOk);
		bot.App.Users.Close(staff.Referee, SessionEndReason.TimedOut);

		bot.App.Clock.Advance(TimeSpan.FromSeconds(10));

		Assert.Equal(bot.Text(AnnouncementReplies.CountdownCancelled), SaidInRoom(bot, staff.Room)[^1]);
		Assert.False(staff.Room.Rounds.InProgress);
		Assert.False(IsRunning(bot, staff.Room));
	}

	[Fact]
	public async Task Elapse_WhenTheStartingUserIsNoLongerAllowedToStart_CancelsTheCountdownAndStartsNoRound()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot, true);
		Assert.True((await StartAsync(bot, staff.Referee, staff.Room, TimeSpan.FromSeconds(10), true)).IsOk);
		Assert.True((await staff.Room.ScopeTo(staff.Creator).Authority.RemoveRefereeAsync(staff.Referee.Record, Token))
			.IsOk);

		bot.App.Clock.Advance(TimeSpan.FromSeconds(10));

		Assert.Equal(bot.Text(AnnouncementReplies.CountdownCancelled), SaidInRoom(bot, staff.Room)[^1]);
		Assert.False(staff.Room.Rounds.InProgress);
	}

	[Fact]
	public async Task Elapse_ForARoundWithoutABeatmap_SaysTheMatchCannotStart()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);
		Assert.True((await StartAsync(bot, staff.Creator, staff.Room, TimeSpan.FromSeconds(10), true)).IsOk);

		bot.App.Clock.Advance(TimeSpan.FromSeconds(10));

		Assert.Equal(bot.Text(AnnouncementReplies.CountdownElapsedForRound), SaidInRoom(bot, staff.Room)[^1]);
		Assert.Null(staff.Room.Rounds.LastRound);
	}

	[Fact]
	public async Task Cancel_ARunningCountdown_AnswersOkAnnouncesItAndNothingElapses()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);
		Assert.True((await StartAsync(bot, staff.Creator, staff.Room, TimeSpan.FromSeconds(30), false)).IsOk);

		var result = await CancelAsync(bot, staff.Creator, staff.Room);
		bot.App.Clock.Advance(TimeSpan.FromSeconds(60));

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Equal(
		[
			bot.Text(AnnouncementReplies.CountdownStarted, new { seconds = 30 }),
			bot.Text(AnnouncementReplies.CountdownCancelled)
		], SaidInRoom(bot, staff.Room));
	}

	[Fact]
	public async Task Cancel_WithNoCountdown_AnswersNotInProgress()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		var result = await CancelAsync(bot, staff.Creator, staff.Room);

		Assert.Equal(RoomOutcome.NotInProgress, result.Outcome);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Cancel_ByAPlainPlayer_IsRefusedWhetherOrNotACountdownRuns(bool running)
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);
		if (running)
			Assert.True((await StartAsync(bot, staff.Creator, staff.Room, TimeSpan.FromMinutes(1), false)).IsOk);

		var result = await CancelAsync(bot, staff.Player, staff.Room);

		Assert.Equal(RoomOutcome.RoleDenied, result.Outcome);
		Assert.Equal(running, IsRunning(bot, staff.Room));
	}

	[Fact]
	public async Task Cancel_InAClosedRoom_AnswersRoomClosed()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);
		Assert.True((await staff.Room.ScopeTo(staff.Creator).CloseAsync(Token)).IsOk);

		var result = await CancelAsync(bot, staff.Creator, staff.Room);

		Assert.Equal(RoomOutcome.RoomClosed, result.Outcome);
	}

	[Fact]
	public async Task RoomClosing_CancelsTheCountdown()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);
		Assert.True((await StartAsync(bot, staff.Creator, staff.Room, TimeSpan.FromMinutes(1), false)).IsOk);

		Assert.True((await staff.Room.ScopeTo(staff.Creator).CloseAsync(Token)).IsOk);

		Assert.False(IsRunning(bot, staff.Room));
	}

	[Fact]
	public async Task RoundStartedByHand_CancelsTheCountdownAndSaysSo()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot, true);
		Assert.True((await StartAsync(bot, staff.Creator, staff.Room, TimeSpan.FromMinutes(1), false)).IsOk);

		Assert.True((await staff.Room.ScopeTo(staff.Creator).Rounds.StartAsync(Token)).IsOk);

		Assert.False(IsRunning(bot, staff.Room));
		Assert.Equal(bot.Text(AnnouncementReplies.CountdownCancelledByRoundStart), SaidInRoom(bot, staff.Room)[^1]);
	}

	[Fact]
	public async Task RoundAborted_CancelsTheCountdownAndSaysSo()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot, true);
		Assert.True((await staff.Room.ScopeTo(staff.Creator).Rounds.StartAsync(Token)).IsOk);
		Assert.True((await StartAsync(bot, staff.Creator, staff.Room, TimeSpan.FromMinutes(1), false)).IsOk);

		Assert.True((await staff.Room.ScopeTo(staff.Creator).Rounds.AbortAsync(Token)).IsOk);

		Assert.False(IsRunning(bot, staff.Room));
		Assert.Equal(bot.Text(AnnouncementReplies.CountdownCancelledByRoundAbort), SaidInRoom(bot, staff.Room)[^1]);
	}

	[Fact]
	public async Task SettingsOfTheRoundChanged_CancelsACountdownThatStartsARoundAndSaysSo()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot, true);
		Assert.True((await StartAsync(bot, staff.Creator, staff.Room, TimeSpan.FromMinutes(1), true)).IsOk);

		Assert.True((await staff.Room.ScopeTo(staff.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(WinCondition: GameWinCondition.Accuracy), Token)).IsOk);

		Assert.False(IsRunning(bot, staff.Room));
		Assert.Equal(bot.Text(AnnouncementReplies.SettingsChangedCountdownCancelled), SaidInRoom(bot, staff.Room)[^1]);
	}

	[Fact]
	public async Task SettingsOfTheRoundChanged_LeavesATimerRunning()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot, true);
		Assert.True((await StartAsync(bot, staff.Creator, staff.Room, TimeSpan.FromMinutes(1), false)).IsOk);

		Assert.True((await staff.Room.ScopeTo(staff.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(WinCondition: GameWinCondition.Accuracy), Token)).IsOk);

		Assert.True(IsRunning(bot, staff.Room));
	}

	[Fact]
	public async Task SettingsUnrelatedToTheRoundChanged_LeavesTheCountdownRunning()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot, true);
		Assert.True((await StartAsync(bot, staff.Creator, staff.Room, TimeSpan.FromMinutes(1), true)).IsOk);

		Assert.True((await staff.Room.ScopeTo(staff.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange("Renamed"), Token)).IsOk);

		Assert.True(IsRunning(bot, staff.Room));
	}

	private static async Task<StaffRoom> SetUpAsync(BotHarness bot, bool withBeatmap = false)
	{
		await bot.StartBotAsync();
		CountdownEvents.Deliver(bot);
		var staff = await StaffRoom.OpenAsync(bot, "Finals");
		if (withBeatmap)
			Assert.True((await staff.Room.ScopeTo(staff.Creator).Settings.ConfigureAsync(
				new RoomSettingsChange(Beatmap: RoomFixture.Beatmap), Token)).IsOk);

		return staff;
	}

	private static async Task<Result<RoomOutcome>> StartAsync(BotHarness bot,
		Connection actor, Room room, TimeSpan length, bool startsRound)
	{
		return await bot.App.Sender.Send(new StartRoomCountdown(actor, room, length, startsRound), Token);
	}

	private static async Task<Result<RoomOutcome>> CancelAsync(BotHarness bot,
		Connection actor, Room room)
	{
		return await bot.App.Sender.Send(new CancelRoomCountdown(actor, room), Token);
	}

	private static List<string> SaidInRoom(BotHarness bot, Room room)
	{
		return bot.SaidIn(bot.App.RoomChannels.Of(room));
	}

	private static bool IsRunning(BotHarness bot, Room room)
	{
		return bot.App.Provider.GetRequiredService<RoomCountdowns>().IsRunning(room);
	}
}