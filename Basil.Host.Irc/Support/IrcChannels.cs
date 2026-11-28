using Basil.Application.Shared.Replies;
using Basil.Application.Chat.Objects;
using Basil.Domain.Users;
using Basil.Host.Irc.Clients;
using Basil.Protocol.Irc;
using Basil.Protocol.Irc.Models;

namespace Basil.Host.Irc.Support;

internal static class IrcChannels
{
	public static void SendTopic(IrcSocket session, Channel channel, IReplies<IrcReply> replies)
	{
		if (string.IsNullOrEmpty(channel.Topic))
			session.Numeric(IrcNumericReply.RplNoTopic, channel.Name, replies.Text(IrcReply.NoTopic));
		else
			session.Numeric(IrcNumericReply.RplTopic, channel.Name, channel.Topic.ReplaceLineEndings(" "));
	}

	public static void SendNames(IrcSocket session, Channel channel, IReplies<IrcReply> replies)
	{
		var entries = channel.Members.GroupBy(member => member.Record)
			.OrderBy(group => IrcView.Nick(group.Key), StringComparer.OrdinalIgnoreCase)
			.Select(group => IrcView.Prefix(channel.RoleOf(group.First()), session.Has("multi-prefix")) +
			                 (session.Has("userhost-in-names")
				                 ? IrcView.Mask(group.Key, session.Domain).ToString()
				                 : IrcView.Nick(group.Key)));
		foreach (var line in IrcText.Pack(entries))
			session.Numeric(IrcNumericReply.RplNamReply, "=", channel.Name, line);
		session.Numeric(IrcNumericReply.RplEndOfNames, channel.Name, replies.Text(IrcReply.EndOfNames));
	}

	public static void SendJoin(IrcSocket session, Channel channel, UserRecord user, IReplies<IrcReply> replies)
	{
		var mask = IrcView.Mask(user, session.Domain);
		session.Deliver(client =>
		{
			client.Send(client.Has("extended-join")
				? IrcMessage.Join(mask, channel.Name, user.Value.Name, IrcView.Nick(user))
				: IrcMessage.Join(mask, channel.Name));
			SendTopic(client, channel, replies);
			SendNames(client, channel, replies);
		});
	}
}