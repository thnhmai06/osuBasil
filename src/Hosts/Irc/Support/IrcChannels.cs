using System.Text;
using Basil.Application.Chat.Objects;
using Basil.Domain.Users;
using Basil.Host.Irc.Clients;
using Basil.Protocol.Irc.Models;

namespace Basil.Host.Irc.Support;

internal static class IrcChannels
{
	public static void SendTopic(IrcSocket session, Channel channel)
	{
		if (string.IsNullOrEmpty(channel.Topic))
			session.Numeric(IrcNumericReply.RplNoTopic, channel.Name, "No topic is set");
		else
			session.Numeric(IrcNumericReply.RplTopic, channel.Name, channel.Topic.ReplaceLineEndings(" "));
	}

	public static void SendNames(IrcSocket session, Channel channel)
	{
		var now = session.Now;
		var entries = channel.Members.GroupBy(member => member.Record)
			.OrderBy(group => IrcView.Nick(group.Key), StringComparer.OrdinalIgnoreCase)
			.Select(group => IrcView.Prefix(channel, group.First(), now, session.Has("multi-prefix")) +
			                 (session.Has("userhost-in-names")
				                 ? IrcView.Mask(group.Key, session.Domain).ToString()
				                 : IrcView.Nick(group.Key)));
		var line = new StringBuilder();
		foreach (var entry in entries)
		{
			if (line.Length > 0 && line.Length + entry.Length > IrcReplies.MaxChunkBytes)
			{
				session.Numeric(IrcNumericReply.RplNamReply, "=", channel.Name, line.ToString());
				line.Clear();
			}

			if (line.Length > 0) line.Append(' ');
			line.Append(entry);
		}

		if (line.Length > 0) session.Numeric(IrcNumericReply.RplNamReply, "=", channel.Name, line.ToString());
		session.Numeric(IrcNumericReply.RplEndOfNames, channel.Name, "End of /NAMES list.");
	}

	public static void SendJoin(IrcSocket session, Channel channel, UserRecord user)
	{
		var mask = IrcView.Mask(user, session.Domain).ToString();
		session.Deliver(client =>
		{
			client.Send(client.Has("extended-join")
				? new IrcMessage("JOIN", mask, channel.Name, user.Value.Name, IrcView.Nick(user))
				: new IrcMessage("JOIN", mask, channel.Name));
			SendTopic(client, channel);
			SendNames(client, channel);
		});
	}
}