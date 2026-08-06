using Basil.Application.Configurations;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Irc;
using Basil.Domain.Users;
using Basil.Protocol.Irc;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.Application.Tests.Sessions;

/// <summary>
///     Covers <see cref="ChannelMembershipService.DisconnectFromChannels" />'s PART-vs-QUIT decision
///     tree (plan test 25): the outcome depends only on whether the disconnecting UserId still has
///     another live session anywhere in the chat system, never on which session type disappeared.
/// </summary>
public class ChannelDisconnectSemanticsTests
{
	private readonly IChannelRegistry _channelRegistry = Substitute.For<IChannelRegistry>();
	private readonly IUserSessionRegistry _sessionRegistry = Substitute.For<IUserSessionRegistry>();

	private ChannelMembershipService MakeService()
	{
		return new ChannelMembershipService(_sessionRegistry, _channelRegistry, Options.Create(new IrcOptions()));
	}

	private static GameSession MakeGame(int id, string name)
	{
		return new GameSession(id, name, $"token-{id}", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
	}

	private static IrcSession MakeIrc(int id, string name)
	{
		return new IrcSession(id, name, $"irc-{id}", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
		{
			Connection = new RecordingIrcConnection()
		};
	}

	private void Register(params UserSession[] sessions)
	{
		foreach (var group in sessions.GroupBy(s => s.Id))
			_sessionRegistry.GetSessionsByUserId(group.Key).Returns(group.ToList());

		_sessionRegistry.GameSessions.Returns(sessions.OfType<GameSession>().ToList());
	}

	[Fact]
	public void IrcDisconnects_GameSessionOfSameUserIdStillInChannel_NoPartNoQuit()
	{
		var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
		var game = MakeGame(1, "alice");
		var irc = MakeIrc(1, "alice");
		var otherIrc = MakeIrc(2, "bob");
		game.JoinChannel("#osu");
		irc.JoinChannel("#osu");
		otherIrc.JoinChannel("#osu");
		channel.Join(game.Id);
		channel.Join(irc.Id);
		channel.Join(otherIrc.Id);
		_channelRegistry.GetByName("#osu").Returns(channel);
		Register(game, irc, otherIrc);

		MakeService().DisconnectFromChannels(irc, "Connection closed");

		Assert.True(channel.Contains(1)); // game session keeps UserId 1 in the roster
		var received = ((RecordingIrcConnection)otherIrc.Connection).Received;
		Assert.DoesNotContain(received, m => m.Command is "PART" or "QUIT");
	}

	[Fact]
	public void GameLogsOut_IrcSessionOfSameUserIdStillInChannel_NoPartNoQuit()
	{
		var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
		var game = MakeGame(1, "alice");
		var irc = MakeIrc(1, "alice");
		var otherIrc = MakeIrc(2, "bob");
		game.JoinChannel("#osu");
		irc.JoinChannel("#osu");
		otherIrc.JoinChannel("#osu");
		channel.Join(game.Id);
		channel.Join(irc.Id);
		channel.Join(otherIrc.Id);
		_channelRegistry.GetByName("#osu").Returns(channel);
		Register(game, irc, otherIrc);

		MakeService().DisconnectFromChannels(game, "Logged out");

		Assert.True(channel.Contains(1)); // irc session keeps UserId 1 in the roster
		var received = ((RecordingIrcConnection)otherIrc.Connection).Received;
		Assert.DoesNotContain(received, m => m.Command is "PART" or "QUIT");
	}

	[Fact]
	public void GameLogsOut_IrcSessionOfSameUserIdInADifferentChannel_PartsOnlyTheDisconnectedChannel()
	{
		var osu = new ChannelSession(1, "#osu", "General", 0, 0, true);
		var chat = new ChannelSession(2, "#chat", "Chat", 0, 0, true);
		var game = MakeGame(1, "alice");
		var irc = MakeIrc(1, "alice");
		var otherIrc = MakeIrc(2, "bob");
		game.JoinChannel("#osu");
		irc.JoinChannel("#chat");
		otherIrc.JoinChannel("#osu");
		osu.Join(game.Id);
		osu.Join(otherIrc.Id);
		chat.Join(irc.Id);
		_channelRegistry.GetByName("#osu").Returns(osu);
		_channelRegistry.GetByName("#chat").Returns(chat);
		Register(game, irc, otherIrc);

		MakeService().DisconnectFromChannels(game, "Logged out");

		Assert.False(osu.Contains(1)); // #osu: game session, the only presence there, is gone
		Assert.True(chat.Contains(1)); // #chat: irc session is untouched
		var received = ((RecordingIrcConnection)otherIrc.Connection).Received;
		Assert.Contains(received, m => m.Command == "PART");
		Assert.DoesNotContain(received, m => m.Command == "QUIT");
	}

	[Fact]
	public void LastSessionOfUserId_Disconnects_SendsExactlyOneQuit_NoPart()
	{
		var osu = new ChannelSession(1, "#osu", "General", 0, 0, true);
		var chat = new ChannelSession(2, "#chat", "Chat", 0, 0, true);
		var irc = MakeIrc(1, "alice"); // this UserId's only session
		var otherIrc = MakeIrc(2, "bob");
		irc.JoinChannel("#osu");
		irc.JoinChannel("#chat");
		otherIrc.JoinChannel("#osu");
		otherIrc.JoinChannel("#chat");
		osu.Join(irc.Id);
		osu.Join(otherIrc.Id);
		chat.Join(irc.Id);
		chat.Join(otherIrc.Id);
		_channelRegistry.GetByName("#osu").Returns(osu);
		_channelRegistry.GetByName("#chat").Returns(chat);
		Register(irc, otherIrc);

		MakeService().DisconnectFromChannels(irc, "Connection closed");

		Assert.False(osu.Contains(1));
		Assert.False(chat.Contains(1));
		var received = ((RecordingIrcConnection)otherIrc.Connection).Received;
		Assert.DoesNotContain(received, m => m.Command == "PART");
		Assert.Single(received, m => m.Command == "QUIT"); // exactly one QUIT, not one per shared channel
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
