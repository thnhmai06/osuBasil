using Basil.Application.Abstractions.Settings;
using Basil.Application.Configurations;
using Basil.Application.Services.Content;
using Basil.Application.Services.Irc;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Irc;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Users;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.Application.Tests.Services.Irc;

/// <summary>
///     Covers the read-only IRC query replies: what each command answers, and — the rule that is easy
///     to break — that an instance channel such as a match room is never named to someone who is not
///     already in it, since JOIN is gated on read privilege alone.
/// </summary>
public class IrcQueryServiceTests
{
	private readonly IChannelRegistry _channelRegistry = Substitute.For<IChannelRegistry>();
	private readonly ISessionRegistry<GameSession> _gameRegistry = Substitute.For<ISessionRegistry<GameSession>>();
	private readonly ISessionRegistry<IrcSession> _ircRegistry = Substitute.For<ISessionRegistry<IrcSession>>();
	private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();

	private IrcQueryService MakeService()
	{
		var options = Options.Create(new IrcOptions { Name = "basil.local" });
		var membership = new ChannelMembershipService(_gameRegistry, _ircRegistry, _channelRegistry,
			Substitute.For<IMatchRegistry>(), Substitute.For<IMatchLiveEvents>(), options);
		return new IrcQueryService(_channelRegistry, _gameRegistry, _ircRegistry, membership,
			new MotdService(_settings), options);
	}

	private static IrcSession MakeIrc(int id, string name, UserPrivileges privilege = UserPrivileges.Unrestricted)
	{
		return new IrcSession(id, name, $"irc-{id}", privilege, DateTimeOffset.UnixEpoch)
		{
			IrcConnection = Substitute.For<IIrcConnection>()
		};
	}

	private void Register(params UserSession[] sessions)
	{
		_gameRegistry.All.Returns(sessions.OfType<GameSession>().ToList());
		_ircRegistry.All.Returns(sessions.OfType<IrcSession>().ToList());
		foreach (var game in sessions.OfType<GameSession>())
		{
			_gameRegistry.GetByUserId(game.Id).Returns(game);
			_gameRegistry.GetByName(game.Name).Returns(game);
		}

		foreach (var irc in sessions.OfType<IrcSession>())
		{
			_ircRegistry.GetByUserId(irc.Id).Returns(irc);
			_ircRegistry.GetByName(irc.Name).Returns(irc);
		}
	}

	[Fact]
	public void BuildWelcomeBurst_EmitsTheFourRegistrationNumerics()
	{
		var burst = MakeService().BuildWelcomeBurst("alice").ToList();

		Assert.Equal(["002", "003", "004", "005"], burst.Select(m => m.Command));
		Assert.All(burst, m => Assert.Equal("basil.local", m.Prefix));
		// An empty parameter would serialize into a malformed line, so the user-mode slot is a dash.
		Assert.DoesNotContain("", burst[2].Params);
	}

	[Fact]
	public void BuildWhoisReply_KnownUser_ReportsChannelsButNeverAnInstanceChannel()
	{
		var alice = MakeIrc(1, "alice");
		var bob = MakeIrc(2, "bob");
		var general = new ChannelSession(1, "#osu", 0, 0, true);
		var match = new ChannelSession(0, "#mp_5", 0, 0, false, "#multiplayer", true);
		general.Join(bob.Id);
		match.Join(bob.Id);
		match.Join(alice.Id);
		_channelRegistry.All.Returns([general, match]);
		Register(alice, bob);

		var replies = MakeService().BuildWhoisReply(alice, "bob").ToList();

		Assert.Equal(["311", "319", "312", "317", "318"], replies.Select(m => m.Command));
		Assert.Equal("#osu", replies[1].Params[2]);
	}

	[Fact]
	public void BuildWhoisReply_AwayUser_IncludesTheAwayMessage()
	{
		var alice = MakeIrc(1, "alice");
		var bob = MakeIrc(2, "bob");
		bob.AwayMessage = "brb";
		_channelRegistry.All.Returns([]);
		Register(alice, bob);

		var replies = MakeService().BuildWhoisReply(alice, "bob").ToList();

		var away = Assert.Single(replies, m => m.Command == "301");
		Assert.Equal("brb", away.Params[2]);
	}

	[Fact]
	public void BuildWhoisReply_UnknownNick_ReportsNoSuchNick()
	{
		var alice = MakeIrc(1, "alice");
		Register(alice);

		var replies = MakeService().BuildWhoisReply(alice, "ghost").ToList();

		Assert.Equal(["401", "318"], replies.Select(m => m.Command));
	}

	[Fact]
	public void BuildWhoReply_Channel_ReportsOneEntryPerMemberThenEndOfWho()
	{
		var alice = MakeIrc(1, "alice");
		var bob = MakeIrc(2, "bob", UserPrivileges.Unrestricted | UserPrivileges.Moderator);
		var general = new ChannelSession(1, "#osu", 0, 0, true);
		general.Join(alice.Id);
		general.Join(bob.Id);
		_channelRegistry.GetByName("#osu").Returns(general);
		Register(alice, bob);

		var replies = MakeService().BuildWhoReply(alice, "#osu").ToList();

		Assert.Equal(["352", "352", "315"], replies.Select(m => m.Command));
		Assert.Contains(replies, m => m.Command == "352" && m.Params[6] == "H@");
	}

	[Fact]
	public void BuildNamesReply_InstanceChannelTheRequesterIsNotIn_ReportsOnlyTheEndMarker()
	{
		var alice = MakeIrc(1, "alice");
		var match = new ChannelSession(0, "#mp_5", 0, 0, false, "#multiplayer", true);
		match.Join(2);
		_channelRegistry.GetByName("#mp_5").Returns(match);
		Register(alice);

		var replies = MakeService().BuildNamesReply(alice, "#mp_5").ToList();

		Assert.Equal(["366"], replies.Select(m => m.Command));
	}

	[Fact]
	public void BuildTopicReply_ReadsTheTopicAndRefusesToChangeIt()
	{
		var alice = MakeIrc(1, "alice");
		var general = new ChannelSession(1, "#osu", 0, 0, true);
		var quiet = new ChannelSession(2, "#quiet", 0, 0, true) { Topic = "" };
		_channelRegistry.GetByName("#osu").Returns(general);
		_channelRegistry.GetByName("#quiet").Returns(quiet);
		var service = MakeService();

		Assert.Equal("332", service.BuildTopicReply(alice, "#osu", null).Command);
		Assert.Equal("331", service.BuildTopicReply(alice, "#quiet", null).Command);
		Assert.Equal("482", service.BuildTopicReply(alice, "#osu", "new topic").Command);
		Assert.Equal("403", service.BuildTopicReply(alice, "#ghost", null).Command);
	}

	[Fact]
	public void BuildChannelModeReply_MarksAPrivilegedChannelSecretAndRefusesChanges()
	{
		var alice = MakeIrc(1, "alice", UserPrivileges.Unrestricted | UserPrivileges.Moderator);
		var general = new ChannelSession(1, "#osu", 0, 0, true);
		var staff = new ChannelSession(2, "#staff", UserPrivileges.Moderator, 0, false);
		_channelRegistry.GetByName("#osu").Returns(general);
		_channelRegistry.GetByName("#staff").Returns(staff);
		var service = MakeService();

		Assert.Equal("+nt", service.BuildChannelModeReply(alice, "#osu", null).Params[2]);
		Assert.Equal("+nts", service.BuildChannelModeReply(alice, "#staff", null).Params[2]);
		Assert.Equal("482", service.BuildChannelModeReply(alice, "#osu", "+m").Command);
	}

	[Fact]
	public async Task BuildMotdReplyAsync_ReportsEachConfiguredLineOrThatNoneIsSet()
	{
		var alice = MakeIrc(1, "alice");
		var service = MakeService();

		Assert.Equal(["422"], (await service.BuildMotdReplyAsync(alice)).Select(m => m.Command));

		_settings.GetAsync("Motd", Arg.Any<CancellationToken>()).Returns("first\nsecond");

		Assert.Equal(["375", "372", "372", "376"], (await service.BuildMotdReplyAsync(alice)).Select(m => m.Command));
	}

	[Fact]
	public void BuildIsonReply_ReportsOnlyOnlineNicksUnderTheirStoredSpelling()
	{
		var alice = MakeIrc(1, "alice");
		var bob = MakeIrc(2, "bob");
		Register(alice, bob);
		var service = MakeService();

		// One trailing parameter and several separate ones are both valid spellings of the same list.
		Assert.Equal("alice bob", service.BuildIsonReply(alice, ["alice bob ghost"]).Params[1]);
		Assert.Equal("bob", service.BuildIsonReply(alice, ["ghost", "bob"]).Params[1]);
		Assert.Equal("", service.BuildIsonReply(alice, ["ghost"]).Params[1]);
	}

	[Fact]
	public void BuildLusersReply_CountsAccountsOnceAndSkipsInstanceChannels()
	{
		var alice = MakeIrc(1, "alice");
		var general = new ChannelSession(1, "#osu", 0, 0, true);
		var match = new ChannelSession(0, "#mp_5", 0, 0, false, "#multiplayer", true);
		_channelRegistry.All.Returns([general, match]);
		Register(alice);

		var replies = MakeService().BuildLusersReply(alice).ToList();

		Assert.Equal(["251", "254", "255"], replies.Select(m => m.Command));
		Assert.Contains("1 users", replies[0].Params[1]);
		Assert.Equal("1", replies[1].Params[1]);
	}
}