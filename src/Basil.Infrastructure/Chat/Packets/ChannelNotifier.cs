using Basil.Domain.Channels;
using Basil.Infrastructure.Irc;
using Basil.Infrastructure.Shared.Configuration;
using Basil.Infrastructure.Shared.Sessions;
using Basil.Protocol.Irc;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Chat.Packets;

/// <summary>
///     Tells connected players about channel membership and roster changes, branching per session
///     kind: a bancho packet for a <see cref="GameSession" />, an IRC line for an <see cref="IrcSession" />.
/// </summary>
public sealed class ChannelNotifier(
	ISessionRegistry<GameSession> gameRegistry,
	ISessionRegistry<IrcSession> ircRegistry,
	IOptions<IrcOptions> options) : IChannelNotifier
{
	public void Joined(UserSession self, ChannelSession channel, IReadOnlyList<string> roster)
	{
		switch (self)
		{
			case GameSession game:
				game.Enqueue(ServerPacketWriter.ChannelJoin(channel.DisplayName));
				break;
			case IrcSession irc:
				irc.IrcConnection.Send(IrcMessageWriter.Join(options.Value.Name, irc.Name, irc.Id, channel.Name));
				foreach (var reply in IrcNamesReply.Build(options.Value.Name, irc.Name, channel.Name, roster))
					irc.IrcConnection.Send(reply);
				break;
		}
	}

	public void Left(UserSession self, ChannelSession channel, bool kick)
	{
		switch (self)
		{
			case GameSession game when kick:
				game.Enqueue(ServerPacketWriter.ChannelKick(channel.DisplayName));
				break;
			case IrcSession irc:
				irc.IrcConnection.Send(IrcMessageWriter.Part(options.Value.Name, irc.Name, irc.Id, channel.Name));
				break;
		}
	}

	public void MemberJoined(ChannelSession channel, UserSession member)
	{
		SendToOtherIrcMembers(channel, member.Id,
			IrcMessageWriter.Join(options.Value.Name, member.Name, member.Id, channel.Name));
	}

	public void MemberLeft(ChannelSession channel, UserSession member)
	{
		SendToOtherIrcMembers(channel, member.Id,
			IrcMessageWriter.Part(options.Value.Name, member.Name, member.Id, channel.Name));
	}

	public void Quit(UserSession member, IReadOnlyCollection<int> tell, string reason)
	{
		var message = IrcMessageWriter.Quit(options.Value.Name, member.Name, member.Id, reason);
		foreach (var memberId in tell)
			if (ircRegistry.GetByUserId(memberId) is { } irc)
				irc.IrcConnection.Send(message);
	}

	public void TopicChanged(ChannelSession channel, UserSession by, string topic)
	{
		var message = IrcMessageWriter.Topic(options.Value.Name, by.Name, by.Id, channel.Name, topic);
		foreach (var memberId in channel.MemberIds)
			if (ircRegistry.GetByUserId(memberId) is { } irc)
				irc.IrcConnection.Send(message);
	}

	public void RosterChanged(ChannelSession channel)
	{
		var packet = ServerPacketWriter.ChannelInfo(channel.DisplayName, channel.Topic, channel.PlayerCount);

		if (channel.Instance)
		{
			foreach (var memberId in channel.MemberIds)
				if (gameRegistry.GetByUserId(memberId) is { } game)
					game.Enqueue(packet);
		}
		else
		{
			foreach (var session in gameRegistry.All)
				if (channel.CanRead(session.Privilege))
					session.Enqueue(packet);
		}
	}

	private void SendToOtherIrcMembers(ChannelSession channel, int excludeUserId, IrcMessage message)
	{
		foreach (var memberId in channel.MemberIds)
		{
			if (memberId == excludeUserId) continue;
			if (ircRegistry.GetByUserId(memberId) is { } irc)
				irc.IrcConnection.Send(message);
		}
	}
}