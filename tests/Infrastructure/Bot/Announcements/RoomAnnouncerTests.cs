using Basil.Application.Bot.Announcements;
using Basil.Application.Chat.Objects;
using Basil.Application.Chat.Objects.Events;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Infrastructure.Bot.Handlers;
using Basil.Infrastructure.Bot.Tests.Support;
using Basil.Testing;

namespace Basil.Infrastructure.Bot.Tests.Announcements;

/// <summary>
///     What BasilBot announces when a room's round or closing changes. Expected replies are named by the
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

	private static LobbyRoomClosingAnnounced ClosingIn(BotHarness bot, StaffRoom staff, TimeSpan untilClosing)
	{
		return new LobbyRoomClosingAnnounced(staff.Room, bot.App.Clock.GetUtcNow() + untilClosing);
	}

	private static RoomPlayerJoined JoinedRoom(StaffRoom staff)
	{
		return new RoomPlayerJoined(staff.Room, staff.Player, 2, null, null);
	}

	/// <summary>Has the creator start a round in the room and returns the event the room published for it.</summary>
	private static async Task<RoomRoundStarted> StartRoundAsync(BotHarness bot, StaffRoom staff)
	{
		Assert.True((await staff.Room.ScopeTo(staff.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Beatmap: RoomFixture.Beatmap))) == RoomOutcome.Ok);
		Assert.True((await bot.TypeAsync(staff.Creator, "#osu", "!mp start")).Succeeded);
		return Assert.Single(bot.App.RoomEvents.Events.OfType<RoomRoundStarted>());
	}

	#region Rounds

	[Fact]
	public async Task EmptyRoundStart_CannotStart()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);
		var started = await StartRoundAsync(bot, staff);

		await AnnouncerFor(bot).Handle(started with { Players = [] }, Token);

		Assert.Equal([bot.Text(AnnouncementReplies.RoundStartedNoPlayers)],
			bot.SaidIn(bot.App.RoomChannels.Of(staff.Room)));
	}

	[Fact]
	public async Task PopulatedRoundStart_SaysNothing()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);
		var started = await StartRoundAsync(bot, staff);
		Assert.NotEmpty(started.Players);

		await AnnouncerFor(bot).Handle(started, Token);

		Assert.Empty(bot.SaidIn(bot.App.RoomChannels.Of(staff.Room)));
	}

	[Fact]
	public async Task RoundAborted_AnnouncesAbort()
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
	public async Task MissingScores_NamedPerLine()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);
		var round = staff.Room.Rounds.LastRound ?? (await StartRoundAsync(bot, staff)).Round;
		var announcer = AnnouncerFor(bot);
		await announcer.Handle(new RoomRoundEnded(staff.Room, round, TimeSpan.FromSeconds(10)), Token);
		var decided = new RoomRoundDecided(staff.Room, round, null, [staff.Player.Record, staff.Creator.Record]);

		await announcer.Handle(decided, Token);

		Assert.Equal([
			bot.Text(AnnouncementReplies.ScoresReceivedExcept),
			bot.Text(AnnouncementReplies.ScoreMissing, new { player = staff.Player.Record.Value.Name }),
			bot.Text(AnnouncementReplies.ScoreMissing, new { player = staff.Creator.Record.Value.Name })
		], bot.SaidIn(bot.App.RoomChannels.Of(staff.Room)).Skip(1));
	}

	[Fact]
	public async Task NoMissingScores_AllReceived()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);
		var started = await StartRoundAsync(bot, staff);
		var announcer = AnnouncerFor(bot);
		await announcer.Handle(new RoomRoundEnded(staff.Room, started.Round, TimeSpan.FromSeconds(10)), Token);

		await announcer.Handle(new RoomRoundDecided(staff.Room, started.Round, null, []), Token);

		Assert.Equal([bot.Text(AnnouncementReplies.ScoresReceived)],
			bot.SaidIn(bot.App.RoomChannels.Of(staff.Room)).Skip(1));
	}

	[Fact]
	public async Task RoundNotAnnounced_SaysNothing()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);
		var started = await StartRoundAsync(bot, staff);

		await AnnouncerFor(bot).Handle(new RoomRoundDecided(staff.Room, started.Round, null, []), Token);

		Assert.Empty(bot.SaidIn(bot.App.RoomChannels.Of(staff.Room)));
	}

	[Fact]
	public async Task RoundEnded_TellsNumberWait()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);
		var started = await StartRoundAsync(bot, staff);

		await AnnouncerFor(bot).Handle(
			new RoomRoundEnded(staff.Room, started.Round, TimeSpan.FromSeconds(7)), Token);

		Assert.Equal(
			[bot.Text(AnnouncementReplies.RoundEnded, new { id = started.Round.Index, seconds = 7 })],
			bot.SaidIn(bot.App.RoomChannels.Of(staff.Room)));
	}

	#endregion

	#region Room closing

	[Theory]
	[InlineData(841, 15)]
	[InlineData(900, 15)]
	[InlineData(840, 14)]
	[InlineData(1, 1)]
	public async Task ClosingAnnounced_SaysRoundedMinutes(int secondsLeft,
		int minutes)
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		await AnnouncerFor(bot).Handle(ClosingIn(bot, staff, TimeSpan.FromSeconds(secondsLeft)), Token);

		Assert.Equal([bot.Text(AnnouncementReplies.RoomClosingAnnounced, new { minutes })],
			bot.SaidIn(bot.App.RoomChannels.Of(staff.Room)));
	}

	[Fact]
	public async Task ClosingAnnounced_WhispersCreator()
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
	public async Task ClosingAnnounced_WhispersOnlineReferee()
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
	public async Task ClosingAnnounced_OfflineRefereeSilent()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		await AnnouncerFor(bot).Handle(ClosingIn(bot, staff, TimeSpan.FromMinutes(10)), Token);

		Assert.DoesNotContain(bot.App.ChannelEvents.Events.OfType<ChannelMessagePosted>(),
			posted => posted.Channel is PmChannel pm && pm.Owner.Record.Equals(staff.OfflineReferee));
	}

	[Fact]
	public async Task ClosingAnnounced_PlayersSilent()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		await AnnouncerFor(bot).Handle(ClosingIn(bot, staff, TimeSpan.FromMinutes(10)), Token);

		Assert.Empty(bot.WhisperedTo(staff.Player));
	}

	[Fact]
	public async Task JoinAfterClosing_NoLongerCloses()
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
	public async Task RejoinAfterClosing_SaysNothing()
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
	public async Task UnannouncedRoomJoin_SaysNothing()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		await AnnouncerFor(bot).Handle(JoinedRoom(staff), Token);

		Assert.Empty(bot.SaidIn(bot.App.RoomChannels.Of(staff.Room)));
	}

	[Fact]
	public async Task JoinAfterClose_SaysNothing()
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
	public async Task TwoRoomAnnouncements_TrackedSeparately()
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