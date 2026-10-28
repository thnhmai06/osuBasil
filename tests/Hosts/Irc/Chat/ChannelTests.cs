using Basil.Application.Content.Persistence;
using Basil.Domain.Users;
using Basil.Host.Irc.Tests.Support;
using Basil.Testing.Clients;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Irc.Tests.Chat;

/// <summary>Joining, leaving and reading channels, and the commands IRC may not use to manage them.</summary>
[Collection("IRC host")]
public sealed class ChannelTests(IrcHostFixture fixture)
{
	private BasilHost Host => fixture.Host;

	[Fact]
	public async Task Join_ReadableChannel_SendsJoinTopicAndNames()
	{
		Host.AddChannel("#lounge1", topic: "chill here");
		using var irc = await Host.SignInAsync("JoLounge");

		await irc.SendAsync("JOIN #lounge1");

		Assert.Equal(":JoLounge!JoLounge@basil.test JOIN #lounge1", await irc.ExpectAsync("JOIN"));
		Assert.Equal(":irc.basil.test 332 JoLounge #lounge1 :chill here", await irc.ExpectAsync("332"));
		Assert.Equal(":irc.basil.test 353 JoLounge = #lounge1 :+JoLounge", await irc.ExpectAsync("353"));
		Assert.Equal(":irc.basil.test 366 JoLounge #lounge1 :End of /NAMES list.", await irc.ExpectAsync("366"));
	}

	[Fact]
	public async Task AutoJoinChannel_IsJoinedOnceAtLogin()
	{
		using var irc = await Host.SignInAsync("JoOnce");

		await irc.BarrierAsync();

		Assert.Single(irc.Received, line => IrcClient.CommandOf(line) == "JOIN");
	}

	[Fact]
	public async Task Join_ChannelWithoutTopic_Sends331()
	{
		Host.AddChannel("#lounge2", topic: "");
		using var irc = await Host.SignInAsync("JoNoTopic");

		await irc.SendAsync("JOIN #lounge2");

		Assert.Equal(":irc.basil.test 331 JoNoTopic #lounge2 :No topic is set", await irc.ExpectAsync("331"));
	}

	[Fact]
	public async Task Join_UnknownChannel_Gets403()
	{
		using var irc = await Host.SignInAsync("JoUnknown");

		await irc.SendAsync("JOIN #does-not-exist");

		Assert.Equal(":irc.basil.test 403 JoUnknown #does-not-exist :No such channel", await irc.ExpectAsync("403"));
	}

	[Fact]
	public async Task Join_ChannelTheUserMayNotRead_Gets473()
	{
		Host.AddChannel("#staff1", UserPrivileges.Moderator);
		using var irc = await Host.SignInAsync("JoStaff");

		await irc.SendAsync("JOIN #staff1");

		Assert.Equal(":irc.basil.test 473 JoStaff #staff1 :Cannot join channel", await irc.ExpectAsync("473"));
	}

	[Fact]
	public async Task Join_SpectatorChannel_Gets473()
	{
		using var irc = await Host.SignInAsync("JoSpec");

		await irc.SendAsync("JOIN #spec_1");

		Assert.Equal(":irc.basil.test 473 JoSpec #spec_1 :Cannot join channel", await irc.ExpectAsync("473"));
	}

	[Fact]
	public async Task Join_AChannelAlreadyJoined_AddsNothing()
	{
		using var irc = await Host.SignInAsync("JoTwice");

		await irc.SendAsync("JOIN #osu");
		var lines = await irc.BarrierAsync();

		Assert.Empty(lines);
	}

	[Fact]
	public async Task Join_CommaSeparatedList_JoinsEach()
	{
		Host.AddChannel("#lounge3");
		Host.AddChannel("#lounge4");
		using var irc = await Host.SignInAsync("JoMany");

		await irc.SendAsync("JOIN #lounge3,#lounge4");

		await irc.ExpectFromAsync("JOIN", "#lounge3");
		await irc.ExpectFromAsync("JOIN", "#lounge4");
	}

	[Fact]
	public async Task Join_WithoutParameter_Gets461()
	{
		using var irc = await Host.SignInAsync("JoNothing");

		await irc.SendAsync("JOIN");

		Assert.Equal(":irc.basil.test 461 JoNothing JOIN :Not enough parameters", await irc.ExpectAsync("461"));
	}

	[Fact]
	public async Task Part_JoinedChannel_EchoesPartToTheClient()
	{
		using var irc = await Host.SignInAsync("PaLeave");

		await irc.SendAsync("PART #osu");

		Assert.Equal(":PaLeave!PaLeave@basil.test PART #osu", await irc.ExpectAsync("PART"));
	}

	[Fact]
	public async Task Part_ChannelTheClientIsNotIn_Gets442()
	{
		Host.AddChannel("#lounge5");
		using var irc = await Host.SignInAsync("PaNotIn");

		await irc.SendAsync("PART #lounge5");

		Assert.Equal(":irc.basil.test 442 PaNotIn #lounge5 :You're not on that channel", await irc.ExpectAsync("442"));
	}

	[Fact]
	public async Task Part_UnknownChannel_Gets403()
	{
		using var irc = await Host.SignInAsync("PaUnknown");

		await irc.SendAsync("PART #nope-nope");

		await irc.ExpectAsync("403");
	}

	[Fact]
	public async Task Topic_ReadOnAJoinedChannel_Answers332()
	{
		Host.AddChannel("#topical", topic: "the topic");
		using var irc = await Host.SignInAsync("ToRead");
		await irc.SendAsync("JOIN #topical");
		await irc.ExpectAsync("366");

		await irc.SendAsync("TOPIC #topical");

		Assert.Equal(":irc.basil.test 332 ToRead #topical :the topic", await irc.ExpectAsync("332"));
	}

	[Fact]
	public async Task Topic_Set_Gets482()
	{
		using var irc = await Host.SignInAsync("ToSet");

		await irc.SendAsync("TOPIC #osu :new topic");

		Assert.StartsWith(":irc.basil.test 482 ToSet #osu :", await irc.ExpectAsync("482"));
	}

	[Fact]
	public async Task Topic_OfAChannelTheClientIsNotIn_Gets442()
	{
		Host.AddChannel("#topical2");
		using var irc = await Host.SignInAsync("ToOutside");

		await irc.SendAsync("TOPIC #topical2");

		await irc.ExpectAsync("442");
	}

	[Fact]
	public async Task Mode_ChannelQuery_Answers324WithNoModes()
	{
		using var irc = await Host.SignInAsync("MoQuery");

		await irc.SendAsync("MODE #osu");

		Assert.Equal(":irc.basil.test 324 MoQuery #osu +", await irc.ExpectAsync("324"));
	}

	[Theory]
	[InlineData("MODE #osu +o MoChange", "MoCh1")]
	[InlineData("MODE #osu +b *!*@*", "MoCh2")]
	[InlineData("MODE #osu b", "MoCh3")]
	[InlineData("KICK #osu Somebody", "MoCh4")]
	[InlineData("INVITE Somebody #osu", "MoCh5")]
	public async Task ManagementCommand_Gets482(string line, string name)
	{
		using var irc = await Host.SignInAsync(name);

		await irc.SendAsync(line.Replace("MoChange", name));

		Assert.StartsWith($":irc.basil.test 482 {name} #osu :", await irc.ExpectAsync("482"));
	}

	[Theory]
	[InlineData("KICK #ghost Somebody", "MoGh1")]
	[InlineData("INVITE Somebody #ghost", "MoGh2")]
	[InlineData("MODE #ghost", "MoGh3")]
	public async Task ManagementCommand_OnAnUnknownChannel_Gets403(string line, string name)
	{
		using var irc = await Host.SignInAsync(name);

		await irc.SendAsync(line);

		await irc.ExpectAsync("403");
	}

	[Fact]
	public async Task Mode_OwnUser_AnswersAndOthersAreRefused()
	{
		using var irc = await Host.SignInAsync("MoSelf");

		await irc.SendAsync("MODE MoSelf");
		Assert.Equal(":irc.basil.test 221 MoSelf +", await irc.ExpectAsync("221"));
		await irc.SendAsync("MODE MoSelf +i");
		await irc.ExpectAsync("501");
		await irc.SendAsync("MODE SomeoneElse");
		await irc.ExpectAsync("502");
	}

	[Theory]
	[InlineData("OPER admin pw", "421")]
	[InlineData("VERSION", "421")]
	[InlineData("FROBNICATE", "421")]
	public async Task UnsupportedCommand_Gets421(string line, string numeric)
	{
		using var irc = await Host.SignInAsync("Un" + line[..3]);

		await irc.SendAsync(line);

		Assert.Equal($":irc.basil.test {numeric} Un{line[..3]} {line.Split(' ')[0]} :Unknown command",
			await irc.ExpectAsync(numeric));
	}

	[Fact]
	public async Task Line_LongerThanTheLimit_Gets417AndIsDropped()
	{
		using var irc = await Host.SignInAsync("LiLong");

		await irc.SendAsync("PRIVMSG #osu :" + new string('x', 600));

		Assert.Equal(":irc.basil.test 417 LiLong :Input line was too long", await irc.ExpectAsync("417"));
		await irc.SendAsync("PING after");
		Assert.StartsWith(":irc.basil.test PONG", await irc.ExpectAsync("PONG"));
	}

	[Fact]
	public async Task Ping_IsAnsweredWithPongAndTheSameToken()
	{
		using var irc = await Host.SignInAsync("PiPong");

		await irc.SendAsync("PING token123");

		Assert.Equal(":irc.basil.test PONG irc.basil.test :token123", await irc.ExpectAsync("PONG"));
	}

	[Fact]
	public async Task Keepalive_TheServerPingsAnIdleClientEveryTwoMinutes()
	{
		using var irc = await Host.SignInAsync("PiKeep");

		Host.Time.Advance(TimeSpan.FromMinutes(2));

		Assert.Equal("PING :irc.basil.test", await irc.ExpectAsync("PING"));
	}

	[Fact]
	public async Task Motd_WhenNoneIsSet_Answers422()
	{
		using var irc = await Host.SignInAsync("MoMotd");

		await irc.SendAsync("MOTD");

		Assert.Equal(":irc.basil.test 422 MoMotd :MOTD File is missing", await irc.ExpectAsync("422"));
	}

	[Fact]
	public async Task Motd_WhenSet_IsSentLineByLine()
	{
		var settings = Host.Services.GetRequiredService<ISettingsRepository>();
		var before = await settings.GetAsync();
		await settings.CreateOrUpdateAsync(before with { General = new ServerSettings.GeneralSettings { Motd = "first line\nsecond line" } });
		try
		{
			using var irc = await Host.SignInAsync("MoMotdSet");

			Assert.Contains(":irc.basil.test 375 MoMotdSet :- irc.basil.test Message of the day -", irc.Received);
			Assert.Contains(":irc.basil.test 372 MoMotdSet :- first line", irc.Received);
			Assert.Contains(":irc.basil.test 372 MoMotdSet :- second line", irc.Received);
			Assert.Contains(":irc.basil.test 376 MoMotdSet :End of /MOTD command.", irc.Received);
		}
		finally
		{
			await settings.CreateOrUpdateAsync(before);
		}
	}
}
