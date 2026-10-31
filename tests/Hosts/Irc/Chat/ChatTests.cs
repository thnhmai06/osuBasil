using Basil.Domain.Users;
using Basil.Host.Irc.Support;
using Basil.Host.Irc.Tests.Support;
using Basil.Testing.Clients;

namespace Basil.Host.Irc.Tests.Chat;

/// <summary>Chat between IRC clients and the osu! client, in both directions, and the limits on it.</summary>
[Collection("IRC host")]
public sealed class ChatTests(IrcHostFixture fixture)
{
	private BasilHost Host => fixture.Host;

	private static IEnumerable<ServerPacket> Messages(OsuClient client)
	{
		return client.Received.Where(packet => packet.Id == OsuClient.In.SendMessage);
	}

	[Fact]
	public async Task IrcChannelMessage_ReachesTheOsuClient()
	{
		using var irc = await Host.SignInAsync("ChIrcToOsu");
		using var osu = await Host.LoginOsuAsync("ChOsuListens");
		osu.ClearReceived();

		await irc.SendAsync("PRIVMSG #osu :hello from irc");
		await osu.WaitForAsync(OsuClient.In.SendMessage);

		var heard = Assert.Single(Messages(osu));
		Assert.Equal(["ChIrcToOsu", "hello from irc", "#osu"], heard.Strings(3));
	}

	[Fact]
	public async Task OsuChannelMessage_ReachesTheIrcClientAsAPrivmsgFromTheFixedMask()
	{
		using var irc = await Host.SignInAsync("ChIrcListens");
		using var osu = await Host.LoginOsuAsync("ChOsuToIrc");

		await osu.SendAsync(writer => writer.Packet(OsuClient.Out.SendPublicMessage,
			ClientPacketWriter.Message("hello from osu", "#osu")));

		Assert.Equal(":ChOsuToIrc!ChOsuToIrc@basil.test PRIVMSG #osu :hello from osu",
			await irc.ExpectAsync("PRIVMSG"));
	}

	[Fact]
	public async Task IrcChannelMessage_ReachesAnotherIrcClient_ButNotTheSender()
	{
		using var sender = await Host.SignInAsync("ChSender");
		using var listener = await Host.SignInAsync("ChListener");
		await sender.BarrierAsync();

		await sender.SendAsync("PRIVMSG #osu :to the others");

		Assert.Equal(":ChSender!ChSender@basil.test PRIVMSG #osu :to the others",
			await listener.ExpectAsync("PRIVMSG"));
		var seen = await sender.BarrierAsync();
		Assert.DoesNotContain(seen, line => IrcClient.CommandOf(line) == "PRIVMSG");
	}

	[Fact]
	public async Task Notice_ReachesTheChannelAsANotice()
	{
		using var sender = await Host.SignInAsync("ChNoticer");
		using var listener = await Host.SignInAsync("ChNoticed");

		await sender.SendAsync("NOTICE #osu :heads up");

		Assert.Equal(":ChNoticer!ChNoticer@basil.test NOTICE #osu :heads up", await listener.ExpectAsync("NOTICE"));
	}

	[Fact]
	public async Task PrivateMessage_FromIrcToOsu_ArrivesAddressedToTheRecipient()
	{
		using var irc = await Host.SignInAsync("ChPmIrc");
		using var osu = await Host.LoginOsuAsync("ChPmOsu");
		osu.ClearReceived();

		await irc.SendAsync("PRIVMSG ChPmOsu :psst");
		await osu.WaitForAsync(OsuClient.In.SendMessage);

		Assert.Equal(["ChPmIrc", "psst", "ChPmOsu"], Assert.Single(Messages(osu)).Strings(3));
	}

	[Fact]
	public async Task PrivateMessage_FromOsuToIrc_ArrivesAddressedToTheIrcNick()
	{
		using var irc = await Host.SignInAsync("ChPmTarget");
		using var osu = await Host.LoginOsuAsync("ChPmSender");

		await osu.SendAsync(writer => writer.Packet(OsuClient.Out.SendPrivateMessage,
			ClientPacketWriter.Message("hi irc", "ChPmTarget")));

		Assert.Equal(":ChPmSender!ChPmSender@basil.test PRIVMSG ChPmTarget :hi irc", await irc.ExpectAsync("PRIVMSG"));
	}

	[Fact]
	public async Task PrivateMessage_BetweenIrcClients_IsDelivered()
	{
		using var first = await Host.SignInAsync("ChPmOne");
		using var second = await Host.SignInAsync("ChPmTwo");

		await first.SendAsync("PRIVMSG ChPmTwo :direct");

		Assert.Equal(":ChPmOne!ChPmOne@basil.test PRIVMSG ChPmTwo :direct", await second.ExpectAsync("PRIVMSG"));
	}

	[Fact]
	public async Task PrivateMessage_ToTheSameUsersOtherIrcDevice_ReachesIt()
	{
		var (first, firstKey) = await Host.NewIrcClientAsync("ChTwoDevices");
		using var _ = first;
		var secondKey = await Host.CreateIrcKeyAsync("ChTwoDevices", "second");
		using var second = await IrcClient.ConnectAsync(Host);
		await first.RegisterAsync("ChTwoDevices", firstKey);
		await second.RegisterAsync("ChTwoDevices", secondKey);
		using var friend = await Host.SignInAsync("ChDevFriend");

		await friend.SendAsync("PRIVMSG ChTwoDevices :to both");

		Assert.Equal(":ChDevFriend!ChDevFriend@basil.test PRIVMSG ChTwoDevices :to both",
			await first.ExpectAsync("PRIVMSG"));
		Assert.Equal(":ChDevFriend!ChDevFriend@basil.test PRIVMSG ChTwoDevices :to both",
			await second.ExpectAsync("PRIVMSG"));
	}

	[Fact]
	public async Task PrivateMessage_ToAnOfflineOrUnknownNick_Gets401()
	{
		using var irc = await Host.SignInAsync("ChLonely");

		await irc.SendAsync("PRIVMSG Nobody :anyone?");

		Assert.Equal(":irc.basil.test 401 ChLonely Nobody :No such nick", await irc.ExpectAsync("401"));
	}

	[Fact]
	public async Task BasilBot_AnswersACommandSentAsAPrivateMessage()
	{
		using var irc = await Host.SignInAsync("ChBotAsker");

		await irc.SendAsync("PRIVMSG BasilBot :!roll 1");

		var reply = await irc.ExpectAsync("PRIVMSG");
		Assert.StartsWith(":BasilBot!BasilBot@basil.test PRIVMSG ChBotAsker :ChBotAsker rolls ", reply);
	}

	[Fact]
	public async Task BasilBot_AnswersEachCommandOnce()
	{
		using var irc = await Host.SignInAsync("ChBotOnce");

		await irc.SendAsync("PRIVMSG BasilBot :!where Nobodyone");
		await irc.SendAsync("PRIVMSG BasilBot :!where Nobodytwo");

		Assert.EndsWith(":Nobodyone is not registered.", await irc.ExpectAsync("PRIVMSG"));
		Assert.EndsWith(":Nobodytwo is not registered.", await irc.ExpectAsync("PRIVMSG"));
	}

	[Fact]
	public async Task BasilBot_AnswersACommandSentInAChannel()
	{
		using var irc = await Host.SignInAsync("ChBotChannel");

		await irc.SendAsync("PRIVMSG #osu :!roll 1");

		Assert.StartsWith(":BasilBot!BasilBot@basil.test PRIVMSG #osu :ChBotChannel rolls ",
			await irc.ExpectAsync("PRIVMSG"));
	}

	[Fact]
	public async Task BasilBot_MultiLineReply_ArrivesAsOneLinePerLine()
	{
		using var irc = await Host.SignInAsync("ChBotHelp");

		await irc.SendAsync("PRIVMSG BasilBot :!help");

		var lines = new List<string>();
		for (var i = 0; i < 5; i++) lines.Add(await irc.ExpectAsync("PRIVMSG"));
		Assert.All(lines, line => Assert.StartsWith(":BasilBot!BasilBot@basil.test PRIVMSG ChBotHelp :", line));
		Assert.Contains(lines, line => line.EndsWith("!roll [max] - roll a random number from 0 to max (default 100)"));
	}

	[Fact]
	public async Task OsuMessageWithLineBreaks_ArrivesAsOneLinePerLine()
	{
		using var irc = await Host.SignInAsync("ChBreaksIrc");
		using var osu = await Host.LoginOsuAsync("ChBreaksOsu");

		await osu.SendAsync(writer => writer.Packet(OsuClient.Out.SendPublicMessage,
			ClientPacketWriter.Message("first\nsecond\r\nthird", "#osu")));

		Assert.Equal(":ChBreaksOsu!ChBreaksOsu@basil.test PRIVMSG #osu :first", await irc.ExpectAsync("PRIVMSG"));
		Assert.Equal(":ChBreaksOsu!ChBreaksOsu@basil.test PRIVMSG #osu :second", await irc.ExpectAsync("PRIVMSG"));
		Assert.Equal(":ChBreaksOsu!ChBreaksOsu@basil.test PRIVMSG #osu :third", await irc.ExpectAsync("PRIVMSG"));
	}

	[Fact]
	public async Task OsuMessageLongerThanALine_IsSplitAcrossLines()
	{
		using var irc = await Host.SignInAsync("ChLongIrc");
		using var osu = await Host.LoginOsuAsync("ChLongOsu");
		var text = new string('a', 400) + new string('b', 50);

		await osu.SendAsync(writer => writer.Packet(OsuClient.Out.SendPublicMessage,
			ClientPacketWriter.Message(text, "#osu")));

		Assert.EndsWith(" :" + new string('a', 400), await irc.ExpectAsync("PRIVMSG"));
		Assert.EndsWith(" :" + new string('b', 50), await irc.ExpectAsync("PRIVMSG"));
	}

	[Fact]
	public async Task IrcMessageLongerThanTheLimit_IsCutAt450Characters()
	{
		using var irc = await Host.SignInAsync("ChCutIrc");
		using var osu = await Host.LoginOsuAsync("ChCutOsu");
		osu.ClearReceived();

		await irc.SendAsync("PRIVMSG #osu :" + new string('x', 480));
		await osu.WaitForAsync(OsuClient.In.SendMessage);

		Assert.Equal(IrcReplies.MaxMessageLength, Assert.Single(Messages(osu)).Strings(3)[1].Length);
	}

	[Fact]
	public async Task Flood_BeyondTheChatRateLimit_DropsTheMessageAndTellsTheClient()
	{
		using var irc = await Host.SignInAsync("ChFlood");
		using var osu = await Host.LoginOsuAsync("ChFloodListener");
		osu.ClearReceived();

		for (var i = 0; i < 11; i++) await irc.SendAsync($"PRIVMSG #osu :spam {i}");

		Assert.Equal($":irc.basil.test NOTICE ChFlood :{IrcReplies.ChatTooFast}", await irc.ExpectAsync("NOTICE"));
		await irc.BarrierAsync();
		Assert.True(await osu.WaitUntilAsync(() => Messages(osu).Count() >= 10));
		await osu.PollAsync();
		Assert.Equal(10, Messages(osu).Count());
	}

	[Fact]
	public async Task Flood_WithStandardReplies_IsAFailMessage()
	{
		using var irc = await Host.SignInAsync("ChFloodFail", "standard-replies");

		for (var i = 0; i < 11; i++) await irc.SendAsync($"PRIVMSG #osu :spam {i}");

		Assert.Equal($":irc.basil.test FAIL PRIVMSG RATE_LIMITED #osu :{IrcReplies.ChatTooFast}",
			await irc.ExpectAsync("FAIL"));
	}

	[Fact]
	public async Task Privmsg_WithoutTargetOrText_GetsTheMatchingErrors()
	{
		using var irc = await Host.SignInAsync("ChBadForm");

		await irc.SendAsync("PRIVMSG");
		Assert.Equal(":irc.basil.test 411 ChBadForm :No recipient given (PRIVMSG)", await irc.ExpectAsync("411"));
		await irc.SendAsync("PRIVMSG #osu");
		Assert.Equal(":irc.basil.test 412 ChBadForm :No text to send", await irc.ExpectAsync("412"));
	}

	[Fact]
	public async Task Privmsg_ToAnUnknownChannel_Gets403()
	{
		using var irc = await Host.SignInAsync("ChNoChannel");

		await irc.SendAsync("PRIVMSG #nowhere :hi");

		Assert.Equal(":irc.basil.test 403 ChNoChannel #nowhere :No such channel", await irc.ExpectAsync("403"));
	}

	[Fact]
	public async Task Privmsg_ToAChannelTheClientIsNotIn_Gets404()
	{
		using var irc = await Host.SignInAsync("ChNotIn");
		await irc.SendAsync("PART #osu");
		await irc.ExpectAsync("PART");

		await irc.SendAsync("PRIVMSG #osu :still here?");

		Assert.Equal(":irc.basil.test 404 ChNotIn #osu :Cannot send to channel", await irc.ExpectAsync("404"));
	}

	[Fact]
	public async Task Privmsg_ToAUserWhoBlocksNonFriendMessages_Gets404WithTheReason()
	{
		using var irc = await Host.SignInAsync("ChBlocked");
		using var osu = await Host.LoginOsuAsync("ChBlocker");
		await osu.SendAsync(writer => writer.Int(OsuClient.Out.ToggleBlockNonFriendDms, 1));

		await irc.SendAsync("PRIVMSG ChBlocker :hi");

		Assert.Equal($":irc.basil.test 404 ChBlocked ChBlocker :{IrcReplies.DmBlocked}", await irc.ExpectAsync("404"));
	}

	[Fact]
	public async Task Privmsg_ToAnAwayUser_RepliesWith301()
	{
		using var irc = await Host.SignInAsync("ChAwayAsker");
		using var away = await Host.SignInAsync("ChAwayUser");
		await away.SendAsync("AWAY :gone fishing");
		Assert.Equal(":irc.basil.test 306 ChAwayUser :You have been marked as being away",
			await away.ExpectAsync("306"));

		await irc.SendAsync("PRIVMSG ChAwayUser :are you there");

		Assert.Equal(":irc.basil.test 301 ChAwayAsker ChAwayUser :gone fishing", await irc.ExpectAsync("301"));
	}

	[Fact]
	public async Task Away_WithoutText_ClearsTheMessageWith305()
	{
		using var irc = await Host.SignInAsync("ChBack");
		await irc.SendAsync("AWAY :brb");
		await irc.ExpectAsync("306");

		await irc.SendAsync("AWAY");

		Assert.Equal(":irc.basil.test 305 ChBack :You are no longer marked as being away",
			await irc.ExpectAsync("305"));
	}

	[Fact]
	public async Task Silenced_UserCannotChat_Gets404()
	{
		using var irc = await Host.SignInAsync("ChMute", permissions: UserPermissions.None);
		await irc.SendAsync("PRIVMSG #osu :can I?");

		Assert.StartsWith(":irc.basil.test 404 ChMute #osu :", await irc.ExpectAsync("404"));
	}

	[Fact]
	public async Task EchoMessage_SendsTheSenderItsOwnMessage()
	{
		using var irc = await Host.SignInAsync("ChEcho", "echo-message");

		await irc.SendAsync("PRIVMSG #osu :me too");

		Assert.Equal(":ChEcho!ChEcho@basil.test PRIVMSG #osu :me too", await irc.ExpectAsync("PRIVMSG"));
	}

	[Fact]
	public async Task EchoMessage_OfAPrivateMessage_GoesToTheSender()
	{
		using var irc = await Host.SignInAsync("ChEchoPm", "echo-message");
		using var other = await Host.SignInAsync("ChEchoPmTo");

		await irc.SendAsync("PRIVMSG ChEchoPmTo :hey");

		Assert.Equal(":ChEchoPm!ChEchoPm@basil.test PRIVMSG ChEchoPmTo :hey", await irc.ExpectAsync("PRIVMSG"));
		Assert.Equal(":ChEchoPm!ChEchoPm@basil.test PRIVMSG ChEchoPmTo :hey", await other.ExpectAsync("PRIVMSG"));
	}

	[Fact]
	public async Task ServerTimeAndAccountTag_AreAddedToRelayedMessages()
	{
		using var listener = await Host.SignInAsync("ChTagListener", "server-time account-tag");
		using var sender = await Host.SignInAsync("ChTagSender");
		var now = Host.Time.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");

		await sender.SendAsync("PRIVMSG #osu :tagged");

		Assert.Equal(
			$"@time={now};account=ChTagSender :ChTagSender!ChTagSender@basil.test PRIVMSG #osu :tagged",
			await listener.ExpectAsync("PRIVMSG"));
	}

	[Fact]
	public async Task ClientTags_AreAcceptedAndNotRelayed()
	{
		using var listener = await Host.SignInAsync("ChCtagListener", "message-tags");
		using var sender = await Host.SignInAsync("ChCtagSender", "message-tags");

		await sender.SendAsync("@+draft/reply=abc PRIVMSG #osu :with a tag");

		Assert.Equal(":ChCtagSender!ChCtagSender@basil.test PRIVMSG #osu :with a tag",
			await listener.ExpectAsync("PRIVMSG"));
	}

	[Fact]
	public async Task ReadByTheOsuClientsAlias_TheOsuUserIsNotShownTwice()
	{
		using var irc = await Host.SignInAsync("ChDouble");
		using var osu = await Host.LoginOsuAsync("ChDoubleOsu");

		var names = await irc.NamesOfAsync("#osu");

		Assert.Equal(1, names.Count(entry => entry.EndsWith("ChDoubleOsu", StringComparison.Ordinal)));
	}
}