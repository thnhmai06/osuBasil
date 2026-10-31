using Basil.Domain.Users;
using Basil.Host.Irc.Tests.Support;
using Basil.Testing.Clients;

namespace Basil.Host.Irc.Tests.Chat;

/// <summary>The <c>#mp_{id}</c> channels follow the rules of the room, whichever client asks.</summary>
[Collection("IRC host")]
public sealed class RoomChannelTests(IrcHostFixture fixture)
{
	private BasilHost Host => fixture.Host;

	private static int MatchIdOf(ServerPacket joinSuccess)
	{
		return BitConverter.ToUInt16(joinSuccess.Payload, 0);
	}

	/// <summary>An account that has an osu! client seated as host of a new room and an IRC client of its own.</summary>
	private async Task<(OsuClient Osu, IrcClient Irc, int RoomId)> CreatorWithIrcAsync(string name)
	{
		var osu = await Host.NewClientAsync(name);
		await osu.LoginAsync();
		var key = await Host.CreateIrcKeyAsync(name);
		await osu.SendAsync(writer =>
			writer.Packet(OsuClient.Out.CreateMatch, ClientPacketWriter.Match("room of " + name, "pw")));
		var id = MatchIdOf((await osu.WaitForAsync(OsuClient.In.MatchJoinSuccess))!);
		var irc = await IrcClient.ConnectAsync(Host);
		await irc.RegisterAsync(name, key);
		return (osu, irc, id);
	}

	[Fact]
	public async Task Join_TheRoomOfWhichTheUserIsCreator_IsAllowed()
	{
		var (osu, irc, id) = await CreatorWithIrcAsync("RmCreator");
		using var _ = osu;
		using var __ = irc;

		await irc.SendAsync($"JOIN #mp_{id}");

		Assert.Equal($":RmCreator!RmCreator@basil.test JOIN #mp_{id}", await irc.ExpectFromAsync("JOIN", "#mp_"));
		Assert.Equal($":irc.basil.test 332 RmCreator #mp_{id} :room of RmCreator", await irc.ExpectAsync("332"));
	}

	[Fact]
	public async Task Join_ARoomOfSomeoneElse_Gets473()
	{
		var (osu, _, id) = await CreatorWithIrcAsync("RmOwner");
		using var _ = osu;
		using var outsider = await Host.SignInAsync("RmOutsider");

		await outsider.SendAsync($"JOIN #mp_{id}");

		Assert.Equal($":irc.basil.test 473 RmOutsider #mp_{id} :Cannot join channel",
			await outsider.ExpectAsync("473"));
	}

	[Fact]
	public async Task Join_ANonExistentRoom_Gets403()
	{
		using var irc = await Host.SignInAsync("RmNoRoom");

		await irc.SendAsync("JOIN #mp_65000");

		Assert.Equal(":irc.basil.test 403 RmNoRoom #mp_65000 :No such channel", await irc.ExpectAsync("403"));
	}

	[Fact]
	public async Task Join_ARoomWithAnAccountThatCanObserveEveryRoom_IsAllowed()
	{
		var (osu, _, id) = await CreatorWithIrcAsync("RmHostOf");
		using var _ = osu;
		using var observer = await Host.SignInAsync("RmObserver", permissions: UserPermissionsGroups.All);

		await observer.SendAsync($"JOIN #mp_{id}");

		await observer.ExpectFromAsync("JOIN", "#mp_");
	}

	[Fact]
	public async Task RoomChat_ReachesTheCreatorsIrcClient_AndTheCreatorIsMarkedAsAuthority()
	{
		var (osu, irc, id) = await CreatorWithIrcAsync("RmChatter");
		using var _ = osu;
		using var __ = irc;
		await irc.SendAsync($"JOIN #mp_{id}");
		await irc.ExpectFromAsync("366", "#mp_");

		await osu.SendAsync(writer => writer.Packet(OsuClient.Out.SendPublicMessage,
			ClientPacketWriter.Message("hello room", "#multiplayer")));

		Assert.Equal($":RmChatter!RmChatter@basil.test PRIVMSG #mp_{id} :hello room", await irc.ExpectAsync("PRIVMSG"));
		await irc.SendAsync($"NAMES #mp_{id}");
		Assert.Contains("@RmChatter", (await irc.ExpectAsync("353")).Split(" :")[1].Split(' '));
	}

	[Fact]
	public async Task IrcMessageInTheRoom_ReachesThePlayerInTheGame()
	{
		var (osu, irc, id) = await CreatorWithIrcAsync("RmSpeaker");
		using var _ = osu;
		using var __ = irc;
		using var player = await Host.NewClientAsync("RmPlayer");
		await player.LoginAsync();
		await player.SendAsync(writer =>
			writer.Packet(OsuClient.Out.JoinMatch, ClientPacketWriter.JoinMatch(id, "pw")));
		Assert.NotNull(await player.WaitForAsync(OsuClient.In.MatchJoinSuccess));
		await irc.SendAsync($"JOIN #mp_{id}");
		await irc.ExpectFromAsync("366", "#mp_");
		player.ClearReceived();

		await irc.SendAsync($"PRIVMSG #mp_{id} :from the referee seat");
		await player.WaitForAsync(OsuClient.In.SendMessage);

		var heard = Assert.Single(player.Received, packet => packet.Id == OsuClient.In.SendMessage);
		Assert.Equal(["RmSpeaker", "from the referee seat", "#multiplayer"], heard.Strings(3));
	}

	[Fact]
	public async Task ListShowsOnlyTheRoomChannelsTheClientCouldJoin()
	{
		var (osu, irc, id) = await CreatorWithIrcAsync("RmLister");
		using var _ = osu;
		using var __ = irc;
		var (otherOsu, _, otherId) = await CreatorWithIrcAsync("RmOtherLister");
		using var ___ = otherOsu;

		await irc.SendAsync("LIST");
		var rows = new List<string>();
		string line;
		while (IrcClient.CommandOf(line = await irc.ReadLineAsync() ?? throw new InvalidOperationException()) !=
		       "323") rows.Add(line);

		Assert.Contains(rows, row => row.Contains($" #mp_{id} "));
		Assert.DoesNotContain(rows, row => row.Contains($" #mp_{otherId} "));
	}

	[Fact]
	public async Task BotCommandFromIrc_RunsWithTheRightsOfTheIrcConnection()
	{
		var (osu, irc, id) = await CreatorWithIrcAsync("RmCommander");
		using var _ = osu;
		using var __ = irc;
		await irc.SendAsync($"JOIN #mp_{id}");
		await irc.ExpectFromAsync("366", "#mp_");

		await irc.SendAsync($"PRIVMSG #mp_{id} :!mp name Renamed room");

		Assert.StartsWith($":BasilBot!BasilBot@basil.test PRIVMSG #mp_{id} :", await irc.ExpectAsync("PRIVMSG"));
	}

	[Fact]
	public async Task BotCommandFromIrc_ByAnOutsider_IsRefusedByTheRoomNotByTheTransport()
	{
		var (osu, _, _) = await CreatorWithIrcAsync("RmOwnerCmd");
		using var _ = osu;
		using var outsider = await Host.SignInAsync("RmOutsiderCmd");

		await outsider.SendAsync("PRIVMSG BasilBot :!mp name Hijack");

		var reply = await outsider.ExpectAsync("PRIVMSG");
		Assert.StartsWith(":BasilBot!BasilBot@basil.test PRIVMSG RmOutsiderCmd :", reply);
	}
}