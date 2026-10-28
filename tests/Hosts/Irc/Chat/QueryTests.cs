using Basil.Domain.Users;
using Basil.Host.Irc.Tests.Support;
using Basil.Testing.Clients;

namespace Basil.Host.Irc.Tests.Chat;

/// <summary>The read-only queries: NAMES, LIST, WHO, WHOIS, ISON and USERHOST.</summary>
[Collection("IRC host")]
public sealed class QueryTests(IrcHostFixture fixture)
{
	private BasilHost Host => fixture.Host;

	[Fact]
	public async Task Names_MarksWritersWithPlus_AndTheBotIsAMember()
	{
		using var irc = await Host.SignInAsync("QnPlus");

		await irc.SendAsync("NAMES #osu");

		var names = (await irc.ExpectAsync("353")).Split(" :")[1].Split(' ');
		Assert.Contains("+QnPlus", names);
		Assert.Contains("+BasilBot", names);
	}

	[Fact]
	public async Task Names_ChannelManagerHasAtAndWithMultiPrefixBoth()
	{
		var permissions = UserPermissionsGroups.Default | UserPermissions.ChatManageChannels;
		using var manager = await Host.SignInAsync("QnManager", "multi-prefix userhost-in-names", permissions);

		await manager.SendAsync("NAMES #osu");

		Assert.Contains("@+QnManager!QnManager@basil.test", (await manager.ExpectAsync("353")).Split(' '));
	}

	[Fact]
	public async Task Names_WithoutMultiPrefix_ShowsOnlyTheHighestSymbol()
	{
		var permissions = UserPermissionsGroups.Default | UserPermissions.ChatManageChannels;
		using var manager = await Host.SignInAsync("QnSingle", permissions: permissions);

		await manager.SendAsync("NAMES #osu");

		Assert.Contains("@QnSingle", (await manager.ExpectAsync("353")).Split(' '));
	}

	[Fact]
	public async Task Names_ReadOnlyMember_HasNoSymbol()
	{
		Host.AddChannel("#readonly1", write: UserPrivileges.Moderator);
		using var irc = await Host.SignInAsync("QnReader");
		await irc.SendAsync("JOIN #readonly1");
		await irc.ExpectFromAsync("366", "#readonly1");

		await irc.SendAsync("NAMES #readonly1");

		Assert.Contains("QnReader", (await irc.ExpectAsync("353")).Split(" :")[1].Split(' '));
	}

	[Fact]
	public async Task Names_UnknownChannel_OnlyEndsTheList()
	{
		using var irc = await Host.SignInAsync("QnNone");

		await irc.SendAsync("NAMES #missing-channel");

		Assert.Equal(":irc.basil.test 366 QnNone #missing-channel :End of /NAMES list.", await irc.ExpectAsync("366"));
	}

	[Fact]
	public async Task Names_ChannelTheUserMayNotRead_IsNotListed()
	{
		Host.AddChannel("#staff2", UserPrivileges.Moderator);
		using var irc = await Host.SignInAsync("QnHidden");

		await irc.SendAsync("NAMES #staff2");

		var lines = await irc.BarrierAsync();
		Assert.DoesNotContain(lines, line => IrcClient.CommandOf(line) == "353");
	}

	[Fact]
	public async Task List_ShowsReadableVisibleChannelsOnly()
	{
		Host.AddChannel("#listed1", topic: "visible one");
		Host.AddChannel("#staff3", UserPrivileges.Moderator, topic: "staff only");
		Host.AddChannel("#secret1", visible: false, topic: "secret");
		using var irc = await Host.SignInAsync("LiLister");

		await irc.SendAsync("LIST");
		await irc.ExpectAsync("321");
		var listing = new List<string>();
		string line;
		while (IrcClient.CommandOf(line = await irc.ReadLineAsync() ?? throw new InvalidOperationException()) != "323") listing.Add(line);

		Assert.Contains(":irc.basil.test 322 LiLister #listed1 0 :visible one", listing);
		Assert.DoesNotContain(listing, row => row.Contains("#staff3"));
		Assert.DoesNotContain(listing, row => row.Contains("#secret1"));
	}

	[Fact]
	public async Task Who_Channel_ListsEachUserOnceWithFlags()
	{
		using var irc = await Host.SignInAsync("WhoAsker");
		using var away = await Host.SignInAsync("WhoAway");
		await away.SendAsync("AWAY :out");
		await away.ExpectAsync("306");

		await irc.SendAsync("WHO #osu");

		var rows = new List<string>();
		string line;
		while (IrcClient.CommandOf(line = await irc.ReadLineAsync() ?? throw new InvalidOperationException()) != "315") rows.Add(line);
		Assert.Contains(":irc.basil.test 352 WhoAsker #osu WhoAway basil.test irc.basil.test WhoAway G+ :0 WhoAway", rows);
		Assert.Contains(":irc.basil.test 352 WhoAsker #osu WhoAsker basil.test irc.basil.test WhoAsker H+ :0 WhoAsker", rows);
		Assert.Equal(1, rows.Count(row => row.Contains(" WhoAway basil.test ")));
		Assert.Equal(":irc.basil.test 315 WhoAsker #osu :End of /WHO list.", line);
	}

	[Fact]
	public async Task Who_Nick_ShowsThatUserWithoutAChannel()
	{
		using var irc = await Host.SignInAsync("WhoNickAsker");
		using var other = await Host.SignInAsync("WhoNickOther");

		await irc.SendAsync("WHO WhoNickOther");

		Assert.Equal(":irc.basil.test 352 WhoNickAsker * WhoNickOther basil.test irc.basil.test WhoNickOther H :0 WhoNickOther",
			await irc.ExpectAsync("352"));
	}

	[Fact]
	public async Task Whois_Online_ShowsTheFixedMaskAccountAndChannels()
	{
		using var asker = await Host.SignInAsync("WsAsker");
		using var other = await Host.SignInAsync("WsOther");

		await asker.SendAsync("WHOIS WsOther");

		Assert.Equal(":irc.basil.test 311 WsAsker WsOther WsOther basil.test * :WsOther", await asker.ExpectAsync("311"));
		Assert.Equal(":irc.basil.test 312 WsAsker WsOther irc.basil.test :Basil", await asker.ExpectAsync("312"));
		Assert.Equal(":irc.basil.test 330 WsAsker WsOther WsOther :is logged in as", await asker.ExpectAsync("330"));
		Assert.Equal(":irc.basil.test 319 WsAsker WsOther :+#osu", await asker.ExpectAsync("319"));
		Assert.Equal(":irc.basil.test 318 WsAsker WsOther :End of /WHOIS list.", await asker.ExpectAsync("318"));
	}

	[Fact]
	public async Task Whois_AwayUser_Shows301()
	{
		using var asker = await Host.SignInAsync("WsAwayAsker");
		using var away = await Host.SignInAsync("WsAwayWho");
		await away.SendAsync("AWAY :lunch");
		await away.ExpectAsync("306");

		await asker.SendAsync("WHOIS WsAwayWho");

		Assert.Equal(":irc.basil.test 301 WsAwayAsker WsAwayWho :lunch", await asker.ExpectAsync("301"));
	}

	[Fact]
	public async Task Whois_UnknownNick_Gets401Then318()
	{
		using var asker = await Host.SignInAsync("WsNobody");

		await asker.SendAsync("WHOIS Ghostly");

		Assert.Equal(":irc.basil.test 401 WsNobody Ghostly :No such nick", await asker.ExpectAsync("401"));
		await asker.ExpectAsync("318");
	}

	[Fact]
	public async Task Whois_OsuUser_IsFoundByNameWithUnderscoreForSpaces()
	{
		using var asker = await Host.SignInAsync("WsSpacer");
		using var osu = await Host.LoginOsuAsync("Ws Space");

		await asker.SendAsync("WHOIS Ws_Space");

		Assert.Equal(":irc.basil.test 311 WsSpacer Ws_Space Ws_Space basil.test * :Ws_Space", await asker.ExpectAsync("311"));
	}

	[Fact]
	public async Task Ison_ListsOnlyTheOnlineNicks()
	{
		using var asker = await Host.SignInAsync("IsAsker");
		using var other = await Host.SignInAsync("IsOnline");

		await asker.SendAsync("ISON IsOnline Nobody IsAsker");

		Assert.Equal(":irc.basil.test 303 IsAsker :IsOnline IsAsker", await asker.ExpectAsync("303"));
	}

	[Fact]
	public async Task Userhost_ShowsTheFixedMaskAndAway()
	{
		using var asker = await Host.SignInAsync("UhAsker");
		using var away = await Host.SignInAsync("UhAway");
		await away.SendAsync("AWAY :x");
		await away.ExpectAsync("306");

		await asker.SendAsync("USERHOST UhAway UhAsker");

		Assert.Equal(":irc.basil.test 302 UhAsker :UhAway=-UhAway@basil.test UhAsker=+UhAsker@basil.test",
			await asker.ExpectAsync("302"));
	}

	[Fact]
	public async Task NoLineShowsARealAddress()
	{
		using var asker = await Host.SignInAsync("NoAddress");
		await asker.SendAsync("WHOIS NoAddress");
		await asker.SendAsync("WHO #osu");
		await asker.SendAsync("USERHOST NoAddress");
		await asker.SendAsync("NAMES #osu");

		await asker.BarrierAsync();

		Assert.DoesNotContain(asker.Received, line => line.Contains("127.0.0.1") || line.Contains("::1"));
	}
}
