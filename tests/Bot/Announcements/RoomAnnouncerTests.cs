using Basil.Application.Chat.Runtime;
using Basil.Application.Multiplayer.Runtime.Events;
using Basil.Application.Multiplayer.Runtime.Rooms;
using Basil.Application.Multiplayer.Runtime;
using Basil.Bot.Announcements;
using Basil.Bot.Tests.Support;
using Basil.Testing;

namespace Basil.Bot.Tests.Announcements;

/// <summary>
///     What BasilBot announces when a room's countdown, round or closing changes. Expected replies are named by the
///     reply that owns the wording, filled with the arguments its documentation names.
/// </summary>
public sealed class RoomAnnouncerTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static RoomAnnouncer AnnouncerFor(BotHarness bot)
	{
		return new RoomAnnouncer(bot.App.Server, bot.App.RoomChannels, bot.App.Users, bot.App.Clock, bot.Replies);
	}

	private static async Task<StaffRoom> SetUpAsync(BotHarness bot)
	{
		await bot.StartBotAsync();
		return await StaffRoom.OpenAsync(bot, "Finals");
	}

	private static RoomCountdown CountdownIn(BotHarness bot, StaffRoom staff, TimeSpan length, bool startsRound)
	{
		return new RoomCountdown(staff.Creator.Record, length, startsRound, bot.App.Clock.GetUtcNow());
	}

	private static LobbyRoomClosingAnnounced ClosingIn(BotHarness bot, StaffRoom staff, TimeSpan untilClosing)
	{
		return new LobbyRoomClosingAnnounced(staff.Room, bot.App.Clock.GetUtcNow() + untilClosing);
	}

	private static RoomPlayerJoined JoinedRoom(StaffRoom staff)
	{
		return new RoomPlayerJoined(staff.Room, staff.Player, 2, null, null);
	}

	private static Task CancelCountdownAsync(BotHarness bot, StaffRoom staff, RoomCountdownCancelReason reason)
	{
		return AnnouncerFor(bot).Handle(
			new RoomCountdownCancelled(staff.Room, CountdownIn(bot, staff, TimeSpan.FromSeconds(60), true), reason),
			Token).AsTask();
	}

	/// <summary>Has the creator start a round in the room and returns the event the room published for it.</summary>
	private static async Task<RoomRoundStarted> StartRoundAsync(BotHarness bot, StaffRoom staff)
	{
		Assert.True((await staff.Room.ScopeTo(staff.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Beatmap: RoomFixture.Beatmap), Token)).IsOk);
		Assert.True((await bot.TypeAsync(staff.Creator, "#osu", "!mp start")).Succeeded);
		return Assert.Single(bot.App.RoomEvents.Events.OfType<RoomRoundStarted>());
	}

	#region Countdown started

	[Fact]
	public async Task CountdownStarted_ThatStartsARound_SaysQueuedWithTheCountdownSeconds()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		await AnnouncerFor(bot).Handle(
			new RoomCountdownStarted(staff.Room, CountdownIn(bot, staff, TimeSpan.FromSeconds(90), true)), Token);

		Assert.Equal([bot.Text(AnnouncementReplies.CountdownStartedForRound, new { seconds = 90 })],
			bot.SaidIn(bot.App.RoomChannels.Of(staff.Room)));
	}

	[Fact]
	public async Task CountdownStarted_ThatStartsNoRound_SaysTheCountdownSeconds()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		await AnnouncerFor(bot).Handle(
			new RoomCountdownStarted(staff.Room, CountdownIn(bot, staff, TimeSpan.FromSeconds(45), false)), Token);

		Assert.Equal([bot.Text(AnnouncementReplies.CountdownStarted, new { seconds = 45 })],
			bot.SaidIn(bot.App.RoomChannels.Of(staff.Room)));
	}

	#endregion

	#region Countdown ticked

	[Fact]
	public async Task CountdownTicked_WithARound_SaysTheRemainingSecondsForTheRound()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		await AnnouncerFor(bot).Handle(
			new RoomCountdownTicked(staff.Room, CountdownIn(bot, staff, TimeSpan.FromSeconds(90), true),
				TimeSpan.FromSeconds(30)), Token);

		Assert.Equal([bot.Text(AnnouncementReplies.CountdownTickedForRound, new { seconds = 30 })],
			bot.SaidIn(bot.App.RoomChannels.Of(staff.Room)));
	}

	[Fact]
	public async Task CountdownTicked_WithoutARound_SaysTheRemainingSeconds()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		await AnnouncerFor(bot).Handle(
			new RoomCountdownTicked(staff.Room, CountdownIn(bot, staff, TimeSpan.FromSeconds(90), false),
				TimeSpan.FromSeconds(30)), Token);

		Assert.Equal([bot.Text(AnnouncementReplies.CountdownTicked, new { seconds = 30 })],
			bot.SaidIn(bot.App.RoomChannels.Of(staff.Room)));
	}

	[Fact]
	public async Task CountdownTicked_With59Point6SecondsLeft_SaysSixtySeconds()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		await AnnouncerFor(bot).Handle(
			new RoomCountdownTicked(staff.Room, CountdownIn(bot, staff, TimeSpan.FromSeconds(90), false),
				TimeSpan.FromSeconds(59.6)), Token);

		Assert.Equal([bot.Text(AnnouncementReplies.CountdownTicked, new { seconds = 60 })],
			bot.SaidIn(bot.App.RoomChannels.Of(staff.Room)));
	}

	[Fact]
	public async Task CountdownTicked_With59Point4SecondsLeft_SaysFiftyNineSeconds()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		await AnnouncerFor(bot).Handle(
			new RoomCountdownTicked(staff.Room, CountdownIn(bot, staff, TimeSpan.FromSeconds(90), false),
				TimeSpan.FromSeconds(59.4)), Token);

		Assert.Equal([bot.Text(AnnouncementReplies.CountdownTicked, new { seconds = 59 })],
			bot.SaidIn(bot.App.RoomChannels.Of(staff.Room)));
	}

	#endregion

	#region Countdown elapsed

	[Fact]
	public async Task CountdownElapsed_ThatStartedARound_WishesGoodLuck()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		await AnnouncerFor(bot).Handle(
			new RoomCountdownElapsed(staff.Room, CountdownIn(bot, staff, TimeSpan.FromSeconds(30), true),
				RoomOutcome.Ok), Token);

		Assert.Equal([bot.Text(AnnouncementReplies.RoundStartedByCountdown)],
			bot.SaidIn(bot.App.RoomChannels.Of(staff.Room)));
	}

	[Fact]
	public async Task CountdownElapsed_ThatWasNotForARound_SaysItFinished()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		await AnnouncerFor(bot).Handle(
			new RoomCountdownElapsed(staff.Room, CountdownIn(bot, staff, TimeSpan.FromSeconds(30), false),
				RoomOutcome.Ok), Token);

		Assert.Equal([bot.Text(AnnouncementReplies.CountdownElapsed)], bot.SaidIn(bot.App.RoomChannels.Of(staff.Room)));
	}

	[Fact]
	public async Task CountdownElapsed_WithNoBeatmapSelected_SaysTheMatchCannotStart()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		await AnnouncerFor(bot).Handle(
			new RoomCountdownElapsed(staff.Room, CountdownIn(bot, staff, TimeSpan.FromSeconds(30), true),
				RoomOutcome.NoBeatmap), Token);

		Assert.Equal([bot.Text(AnnouncementReplies.CountdownElapsedForRound)],
			bot.SaidIn(bot.App.RoomChannels.Of(staff.Room)));
	}

	[Theory]
	[InlineData(RoomOutcome.InProgress)]
	[InlineData(RoomOutcome.RoomClosed)]
	public async Task CountdownElapsed_WithAnyOtherStartResult_SaysNothing(RoomOutcome result)
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		await AnnouncerFor(bot).Handle(
			new RoomCountdownElapsed(staff.Room, CountdownIn(bot, staff, TimeSpan.FromSeconds(30), true), result),
			Token);

		Assert.Empty(bot.SaidIn(bot.App.RoomChannels.Of(staff.Room)));
	}

	#endregion

	#region Countdown cancelled

	[Fact]
	public async Task CountdownCancelled_Aborted_SaysTheCountdownWasAborted()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		await CancelCountdownAsync(bot, staff, RoomCountdownCancelReason.Aborted);

		Assert.Equal([bot.Text(AnnouncementReplies.CountdownCancelled)],
			bot.SaidIn(bot.App.RoomChannels.Of(staff.Room)));
	}

	[Fact]
	public async Task CountdownCancelled_SettingsChanged_SaysTheSettingsChangedTheCountdown()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		await CancelCountdownAsync(bot, staff, RoomCountdownCancelReason.SettingsChanged);

		Assert.Equal([bot.Text(AnnouncementReplies.SettingsChangedCountdownCancelled)],
			bot.SaidIn(bot.App.RoomChannels.Of(staff.Room)));
	}

	[Fact]
	public async Task CountdownCancelled_RoundStarted_SaysTheRoundStartCancelledIt()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		await CancelCountdownAsync(bot, staff, RoomCountdownCancelReason.RoundStarted);

		Assert.Equal([bot.Text(AnnouncementReplies.CountdownCancelledByRoundStart)],
			bot.SaidIn(bot.App.RoomChannels.Of(staff.Room)));
	}

	[Fact]
	public async Task CountdownCancelled_RoundAborted_SaysTheRoundAbortCancelledIt()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		await CancelCountdownAsync(bot, staff, RoomCountdownCancelReason.RoundAborted);

		Assert.Equal([bot.Text(AnnouncementReplies.CountdownCancelledByRoundAbort)],
			bot.SaidIn(bot.App.RoomChannels.Of(staff.Room)));
	}

	#endregion

	#region Rounds

	[Fact]
	public async Task RoundStarted_WithNoPlayers_SaysTheMatchCannotStart()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);
		var started = await StartRoundAsync(bot, staff);

		await AnnouncerFor(bot).Handle(started with { Players = [] }, Token);

		Assert.Equal([bot.Text(AnnouncementReplies.RoundStartedNoPlayers)],
			bot.SaidIn(bot.App.RoomChannels.Of(staff.Room)));
	}

	[Fact]
	public async Task RoundStarted_WithPlayers_SaysNothing()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);
		var started = await StartRoundAsync(bot, staff);
		Assert.NotEmpty(started.Players);

		await AnnouncerFor(bot).Handle(started, Token);

		Assert.Empty(bot.SaidIn(bot.App.RoomChannels.Of(staff.Room)));
	}

	[Fact]
	public async Task RoundAborted_SaysTheMatchWasAborted()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);
		await StartRoundAsync(bot, staff);
		Assert.True((await bot.TypeAsync(staff.Creator, "#osu", "!mp abort")).Succeeded);
		var aborted = Assert.Single(bot.App.RoomEvents.Events.OfType<RoomRoundAborted>());

		await AnnouncerFor(bot).Handle(aborted, Token);

		Assert.Equal([bot.Text(AnnouncementReplies.RoundAborted)], bot.SaidIn(bot.App.RoomChannels.Of(staff.Room)));
	}

	[Fact]
	public async Task RoundDecided_NamesEachPlayerWhoseScoreDidNotCome()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);
		var decided = new RoomRoundDecided(staff.Room,
			staff.Room.Rounds.LastRound ?? (await StartRoundAsync(bot, staff)).Round,
			null, [staff.Player.Record, staff.Creator.Record]);

		await AnnouncerFor(bot).Handle(decided, Token);

		Assert.Equal([
			bot.Text(AnnouncementReplies.ScoreMissing, new { player = staff.Player.Record.Value.Name, seconds = 10 }),
			bot.Text(AnnouncementReplies.ScoreMissing, new { player = staff.Creator.Record.Value.Name, seconds = 10 })
		], bot.SaidIn(bot.App.RoomChannels.Of(staff.Room)));
	}

	[Fact]
	public async Task RoundDecided_WithNobodyMissing_SaysNothing()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);
		var started = await StartRoundAsync(bot, staff);

		await AnnouncerFor(bot).Handle(new RoomRoundDecided(staff.Room, started.Round, null, []), Token);

		Assert.Empty(bot.SaidIn(bot.App.RoomChannels.Of(staff.Room)));
	}

	#endregion

	#region Room closing

	[Theory]
	[InlineData(841, 15)]
	[InlineData(900, 15)]
	[InlineData(840, 14)]
	[InlineData(1, 1)]
	public async Task ClosingAnnounced_SaysTheMinutesUntilClosingRoundedUpInTheRoomChannel(int secondsLeft,
		int minutes)
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		await AnnouncerFor(bot).Handle(ClosingIn(bot, staff, TimeSpan.FromSeconds(secondsLeft)), Token);

		Assert.Equal([bot.Text(AnnouncementReplies.RoomClosingAnnounced, new { minutes })],
			bot.SaidIn(bot.App.RoomChannels.Of(staff.Room)));
	}

	[Fact]
	public async Task ClosingAnnounced_WhispersTheSameTextToTheCreator()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		await AnnouncerFor(bot).Handle(
			ClosingIn(bot, staff, TimeSpan.FromMinutes(14).Add(TimeSpan.FromSeconds(1))),
			Token);

		Assert.Equal([bot.Text(AnnouncementReplies.RoomClosingAnnounced, new { minutes = 15 })],
			bot.WhisperedTo(staff.Creator));
	}

	[Fact]
	public async Task ClosingAnnounced_WhispersTheSameTextToAnOnlineReferee()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		await AnnouncerFor(bot).Handle(
			ClosingIn(bot, staff, TimeSpan.FromMinutes(14).Add(TimeSpan.FromSeconds(1))),
			Token);

		Assert.Equal([bot.Text(AnnouncementReplies.RoomClosingAnnounced, new { minutes = 15 })],
			bot.WhisperedTo(staff.Referee));
	}

	[Fact]
	public async Task ClosingAnnounced_WhispersNothingToAnOfflineReferee()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		await AnnouncerFor(bot).Handle(ClosingIn(bot, staff, TimeSpan.FromMinutes(10)), Token);

		Assert.DoesNotContain(bot.App.ChannelEvents.Events.OfType<ChannelMessagePosted>(),
			posted => posted.Channel is PmChannel pm && pm.Owner.Record.Equals(staff.OfflineReferee));
	}

	[Fact]
	public async Task ClosingAnnounced_WhispersNothingToThePlayersOfTheRoom()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		await AnnouncerFor(bot).Handle(ClosingIn(bot, staff, TimeSpan.FromMinutes(10)), Token);

		Assert.Empty(bot.WhisperedTo(staff.Player));
	}

	[Fact]
	public async Task PlayerJoined_AfterAClosingAnnouncement_SaysTheRoomWillNoLongerClose()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);
		var announcer = AnnouncerFor(bot);
		await announcer.Handle(ClosingIn(bot, staff, TimeSpan.FromMinutes(10)), Token);
		var saidBefore = bot.SaidIn(bot.App.RoomChannels.Of(staff.Room)).Count;

		await announcer.Handle(JoinedRoom(staff), Token);

		Assert.Equal([bot.Text(AnnouncementReplies.RoomPlayerJoinedClosing)],
			bot.SaidIn(bot.App.RoomChannels.Of(staff.Room)).Skip(saidBefore).ToList());
	}

	[Fact]
	public async Task PlayerJoined_ASecondTimeAfterAClosingAnnouncement_SaysNothing()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);
		var announcer = AnnouncerFor(bot);
		await announcer.Handle(ClosingIn(bot, staff, TimeSpan.FromMinutes(10)), Token);
		await announcer.Handle(JoinedRoom(staff), Token);
		var saidBefore = bot.SaidIn(bot.App.RoomChannels.Of(staff.Room)).Count;

		await announcer.Handle(JoinedRoom(staff), Token);

		Assert.Equal(saidBefore, bot.SaidIn(bot.App.RoomChannels.Of(staff.Room)).Count);
	}

	[Fact]
	public async Task PlayerJoined_IntoARoomNeverAnnouncedAsClosing_SaysNothing()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		await AnnouncerFor(bot).Handle(JoinedRoom(staff), Token);

		Assert.Empty(bot.SaidIn(bot.App.RoomChannels.Of(staff.Room)));
	}

	[Fact]
	public async Task PlayerJoined_AfterTheRoomClosed_SaysNothing()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);
		var announcer = AnnouncerFor(bot);
		await announcer.Handle(ClosingIn(bot, staff, TimeSpan.FromMinutes(10)), Token);
		await announcer.Handle(new LobbyRoomClosed(staff.Room, null, [], null), Token);
		var saidBefore = bot.SaidIn(bot.App.RoomChannels.Of(staff.Room)).Count;

		await announcer.Handle(JoinedRoom(staff), Token);

		Assert.Equal(saidBefore, bot.SaidIn(bot.App.RoomChannels.Of(staff.Room)).Count);
	}

	[Fact]
	public async Task ClosingAnnouncements_OfTwoRooms_AreTrackedIndependently()
	{
		using var bot = new BotHarness();
		await bot.StartBotAsync();
		var first = await StaffRoom.OpenAsync(bot, "First");
		var second = await StaffRoom.OpenAsync(bot, "Second");
		var announcer = AnnouncerFor(bot);
		await announcer.Handle(ClosingIn(bot, first, TimeSpan.FromMinutes(10)), Token);
		await announcer.Handle(ClosingIn(bot, second, TimeSpan.FromMinutes(10)), Token);
		await announcer.Handle(new LobbyRoomClosed(first.Room, null, [], null), Token);

		await announcer.Handle(JoinedRoom(first), Token);
		await announcer.Handle(JoinedRoom(second), Token);

		Assert.DoesNotContain(bot.Text(AnnouncementReplies.RoomPlayerJoinedClosing),
			bot.SaidIn(bot.App.RoomChannels.Of(first.Room)));
		Assert.Contains(bot.Text(AnnouncementReplies.RoomPlayerJoinedClosing),
			bot.SaidIn(bot.App.RoomChannels.Of(second.Room)));
	}

	#endregion
}