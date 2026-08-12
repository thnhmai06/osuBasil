using Basil.Application.Abstractions.Bot;
using Basil.Application.Abstractions.Social;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configurations;
using Basil.Application.Services.Bot;
using Basil.Application.Services.Chat;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Irc;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Users;
using Basil.Protocol.Irc;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.Application.Tests.Services.Chat;

/// <summary>
///     Covers what separates a notice from a chat message: it is delivered under the same membership
///     rules, but never provokes an automatic reply — the whole reason the form exists.
/// </summary>
public class ChatDispatchNoticeTests
{
	private readonly IChannelRegistry _channelRegistry = Substitute.For<IChannelRegistry>();
	private readonly ICommandDispatcher _commandDispatcher = Substitute.For<ICommandDispatcher>();
	private readonly ISessionRegistry<GameSession> _gameRegistry = Substitute.For<ISessionRegistry<GameSession>>();
	private readonly ISessionRegistry<IrcSession> _ircRegistry = Substitute.For<ISessionRegistry<IrcSession>>();

	private ChatDispatchService MakeService()
	{
		var membership = new ChannelMembershipService(_gameRegistry, _ircRegistry, _channelRegistry,
			Substitute.For<IMatchRegistry>(), Substitute.For<IMatchLiveEvents>(), Options.Create(new IrcOptions()));
		return new ChatDispatchService(_channelRegistry, _gameRegistry, membership,
			Substitute.For<IUserRepository>(), Substitute.For<IRelationshipRepository>(), _commandDispatcher,
			Substitute.For<IMatchRegistry>(), NullLogger<ChatDispatchService>.Instance);
	}

	private ChannelSession JoinedChannel(params UserSession[] members)
	{
		var channel = new ChannelSession(1, "#osu", 0, 0, true);
		foreach (var member in members)
		{
			channel.Join(member.Id);
			member.JoinChannel("#osu");
		}

		_channelRegistry.GetByName("#osu").Returns(channel);
		return channel;
	}

	[Fact]
	public async Task SendNoticeAsync_CommandPrefixedText_NeverReachesTheCommandDispatcher()
	{
		var sender = new IrcSession(1, "alice", "irc-1", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
		{
			IrcConnection = new RecordingIrcConnection()
		};
		var member = new IrcSession(2, "bob", "irc-2", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
		{
			IrcConnection = new RecordingIrcConnection()
		};
		JoinedChannel(sender, member);
		_ircRegistry.GetByUserId(member.Id).Returns(member);

		await MakeService().SendNoticeAsync(sender, "#osu", "!mp start");

		await _commandDispatcher.DidNotReceiveWithAnyArgs().DispatchAsync(default!, default!, default, default,
			default!);
		var delivered = Assert.Single(((RecordingIrcConnection)member.IrcConnection).Received);
		Assert.Equal("NOTICE", delivered.Command);
		Assert.Equal("!mp start", delivered.Params[1]);
	}

	[Fact]
	public async Task SendNoticeAsync_ReachesABanchoClientAsAnOrdinaryChatLine()
	{
		var sender = new IrcSession(1, "alice", "irc-1", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
		{
			IrcConnection = new RecordingIrcConnection()
		};
		var member = new GameSession(2, "bob", "token-2", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		JoinedChannel(sender, member);
		_gameRegistry.GetByUserId(member.Id).Returns(member);

		await MakeService().SendNoticeAsync(sender, "#osu", "server restarting");

		Assert.Equal(ServerPacketWriter.SendMessage("alice", "server restarting", "#osu", 1), member.Dequeue());
	}

	[Fact]
	public void WrapLines_SplitsOnNewlinesAndWrapsALongLineAtAWordBoundaryWithoutLosingText()
	{
		var word = new string('a', 100);
		var long_ = string.Join(' ', Enumerable.Repeat(word, 40)); // 4039 chars: two messages' worth

		// Blank lines go, trailing whitespace goes, leading indentation is the author's and stays.
		Assert.Equal(["first", " second"], ChatDispatchService.WrapLines("first\n\n second \r\n").ToArray());

		var wrapped = ChatDispatchService.WrapLines(long_).ToArray();

		Assert.Equal(3, wrapped.Length);
		Assert.All(wrapped, line => Assert.True(line.Length <= ChatDispatchService.MaxMessageLength));
		// Nothing is dropped and no word is broken: the pieces rejoin into exactly the original text.
		Assert.Equal(long_, string.Join(' ', wrapped));
	}

	[Fact]
	public void SendAsBot_SaysEachWrappedLineInTheChannelAsTheBot()
	{
		var bot = new GameSession(BotBootstrapService.BotId, "BasilBot", "bot-token", UserPrivileges.Unrestricted,
			DateTimeOffset.UnixEpoch) { IsBot = true };
		var member = new IrcSession(2, "bob", "irc-2", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
		{
			IrcConnection = new RecordingIrcConnection()
		};
		JoinedChannel(member);
		_ircRegistry.GetByUserId(member.Id).Returns(member);
		_gameRegistry.GetByUserId(BotBootstrapService.BotId).Returns(bot);

		var sent = MakeService().SendAsBot(_channelRegistry.GetByName("#osu")!, "line one\nline two");

		Assert.Equal(2, sent);
		var delivered = ((RecordingIrcConnection)member.IrcConnection).Received;
		Assert.Equal(["line one", "line two"], delivered.Select(m => m.Params[1]));
		Assert.All(delivered, m => Assert.StartsWith("BasilBot!", m.Prefix));
	}

	private sealed class RecordingIrcConnection : IIrcConnection
	{
		public List<IrcMessage> Received { get; } = [];
		public UserSession User => throw new NotImplementedException();

		public void Send(IrcMessage message)
		{
			Received.Add(message);
		}
	}
}