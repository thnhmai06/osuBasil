using Basil.Application.Runtime.Anticheat;
using Basil.Application.Runtime.Chat;
using Basil.Bot.Announcements;
using Basil.Bot.Tests.Support;
using Basil.Domain.Bancho;

namespace Basil.Bot.Tests.Announcements;

/// <summary>
///     What BasilBot announces when a player's client reports signs of cheating: a line in the room channel and a
///     private message to the room's online staff. Expected replies are named by the reply that owns the wording.
/// </summary>
public sealed class AnticheatAnnouncerTests
{
	private const BanchoFlags TwoFlags = BanchoFlags.SpeedHackDetected | BanchoFlags.FastPress;

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task PlayerFlagged_InARoom_SaysThePlayerAndTheReasonInTheRoomChannel()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		await AnnouncerFor(bot).Handle(new AnticheatPlayerFlagged(staff.Player, TwoFlags, staff.Room), Token);

		Assert.Equal([
			bot.Text(AnnouncementReplies.AnticheatFlagRoom,
				new { player = "FinalsPlayer", reason = "SpeedHackDetected, FastPress" })
		], bot.SaidIn(staff.Room.Channel));
	}

	[Fact]
	public async Task PlayerFlagged_InARoom_WhispersTheRoomAndTheReasonToTheCreator()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		await AnnouncerFor(bot).Handle(new AnticheatPlayerFlagged(staff.Player, TwoFlags, staff.Room), Token);

		Assert.Equal([ExpectedWhisper(bot, staff)], bot.WhisperedTo(staff.Creator));
	}

	[Fact]
	public async Task PlayerFlagged_InARoom_WhispersTheRoomAndTheReasonToAnOnlineReferee()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		await AnnouncerFor(bot).Handle(new AnticheatPlayerFlagged(staff.Player, TwoFlags, staff.Room), Token);

		Assert.Equal([ExpectedWhisper(bot, staff)], bot.WhisperedTo(staff.Referee));
	}

	[Fact]
	public async Task PlayerFlagged_InARoom_WhispersNothingToAnOfflineReferee()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		await AnnouncerFor(bot).Handle(new AnticheatPlayerFlagged(staff.Player, TwoFlags, staff.Room), Token);

		Assert.DoesNotContain(bot.App.ChannelEvents.Events.OfType<ChannelMessagePosted>(),
			posted => posted.Channel is PmChannel pm && pm.Owner.Record.Equals(staff.OfflineReferee));
	}

	[Fact]
	public async Task PlayerFlagged_InARoom_WhispersNothingToTheFlaggedPlayer()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		await AnnouncerFor(bot).Handle(new AnticheatPlayerFlagged(staff.Player, TwoFlags, staff.Room), Token);

		Assert.Empty(bot.WhisperedTo(staff.Player));
	}

	[Fact]
	public async Task PlayerFlagged_WithCleanAmongTheFlags_NamesOnlyTheRealFlags()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		await AnnouncerFor(bot).Handle(
			new AnticheatPlayerFlagged(staff.Player, BanchoFlags.Clean | BanchoFlags.FastPress, staff.Room),
			Token);

		Assert.Equal([
			bot.Text(AnnouncementReplies.AnticheatFlagRoom,
				new { player = "FinalsPlayer", reason = "FastPress" })
		], bot.SaidIn(staff.Room.Channel));
	}

	[Fact]
	public async Task PlayerFlagged_WithEveryCheatSign_NamesEachFlagAndNotTheGroup()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		await AnnouncerFor(bot).Handle(
			new AnticheatPlayerFlagged(staff.Player, BanchoFlagsGroups.CheatSigns, staff.Room), Token);

		const string reason = "SpeedHackDetected, IncorrectModValue, MultipleOsuClients, ChecksumFailure, "
		                      + "FlashlightChecksumIncorrect, OsuExecutableChecksum, MissingProcessesInList, "
		                      + "FlashlightImageHack, SpinnerHack, TransparentWindow, FastPress, RawMouseDiscrepancy, "
		                      + "RawKeyboardDiscrepancy, HqAssembly, HqFile, RegistryEdits";
		Assert.Equal([bot.Text(AnnouncementReplies.AnticheatFlagRoom, new { player = "FinalsPlayer", reason })],
			bot.SaidIn(staff.Room.Channel));
	}

	[Fact]
	public async Task PlayerFlagged_WithFlagsOutsideTheCheatSigns_NamesThemToo()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		await AnnouncerFor(bot).Handle(
			new AnticheatPlayerFlagged(staff.Player, BanchoFlags.ConsoleOpen | BanchoFlags.Sdl2Library,
				staff.Room),
			Token);

		Assert.Equal([
			bot.Text(AnnouncementReplies.AnticheatFlagRoom,
				new { player = "FinalsPlayer", reason = "ConsoleOpen, Sdl2Library" })
		], bot.SaidIn(staff.Room.Channel));
	}

	[Fact]
	public async Task PlayerFlagged_WithoutARoom_SaysNothing()
	{
		using var bot = new BotHarness();
		var staff = await SetUpAsync(bot);

		await AnnouncerFor(bot).Handle(new AnticheatPlayerFlagged(staff.Player, TwoFlags, null), Token);

		Assert.Empty(bot.App.ChannelEvents.Events.OfType<ChannelMessagePosted>());
	}

	private static AnticheatAnnouncer AnnouncerFor(BotHarness bot)
	{
		return new AnticheatAnnouncer(bot.App.Server, bot.App.Users, bot.Replies);
	}

	private static async Task<StaffRoom> SetUpAsync(BotHarness bot)
	{
		await bot.StartBotAsync();
		return await StaffRoom.OpenAsync(bot, "Finals");
	}

	private static string ExpectedWhisper(BotHarness bot, StaffRoom staff)
	{
		return bot.Text(AnnouncementReplies.AnticheatFlagPm,
			new
			{
				room = staff.Room.Id,
				name = "Finals",
				player = "FinalsPlayer",
				reason = "SpeedHackDetected, FastPress"
			});
	}
}