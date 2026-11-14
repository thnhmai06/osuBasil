using Basil.Application.Chat.Objects;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;
using Basil.Host.Irc.Session;
using Basil.Host.Irc.Support;
using Basil.Protocol.Irc.Models;

namespace Basil.Host.Irc.Commands;

internal sealed partial class IrcCommands
{
	/// <summary>Lists the channels a connection may see: the visible general channels it may read and the rooms it may join.</summary>
	private IEnumerable<Channel> VisibleChannels(Connection actor, DateTimeOffset now)
	{
		var privileges = actor.EffectivePrivileges(now);
		foreach (var channel in general.VisibleTo(actor))
			yield return channel;

		foreach (var room in lobby.Rooms.Values)
			if (!room.IsClosed && (room.Involves(actor, privileges) || privileges.Allows(UserPrivileges.Tournament)))
				yield return roomChannels.Of(room);
	}

	private void Names(IrcSession session, Connection actor, IrcMessage message)
	{
		var names = message.Parameters.Count > 0
			? message.Parameters[0].Split(',', StringSplitOptions.RemoveEmptyEntries)
			: [.. VisibleChannels(actor, session.Now).Select(channel => channel.Name)];
		foreach (var name in names)
			if (Resolve(name) is { } channel &&
			    (channel.Members.Contains(actor) || VisibleChannels(actor, session.Now).Contains(channel)))
				IrcChannels.SendNames(session, channel);
			else
				session.Numeric(IrcNumericReply.RplEndOfNames, name, "End of /NAMES list.");
	}

	private void List(IrcSession session, Connection actor, IrcMessage message)
	{
		session.Numeric(IrcNumericReply.RplListStart, "Channel", "Users  Name");
		var filter = message.Parameters.Count > 0
			? message.Parameters[0].Split(',', StringSplitOptions.RemoveEmptyEntries)
			: null;
		foreach (var channel in VisibleChannels(actor, session.Now)
			         .Where(channel =>
				         filter is null || filter.Contains(channel.Name, StringComparer.OrdinalIgnoreCase)))
			session.Numeric(IrcNumericReply.RplList, channel.Name, IrcView.UserCount(channel).ToString(),
				channel.Topic.ReplaceLineEndings(" "));
		session.Numeric(IrcNumericReply.RplListEnd, "End of /LIST");
	}

	private void Who(IrcSession session, Connection actor, IrcMessage message)
	{
		var mask = message.Parameters.Count > 0 ? message.Parameters[0] : "*";
		var now = session.Now;
		if (mask.StartsWith('#'))
		{
			if (Resolve(mask) is { } channel &&
			    (channel.Members.Contains(actor) || VisibleChannels(actor, now).Contains(channel)))
				foreach (var group in channel.Members.GroupBy(member => member.Record)
					         .OrderBy(group => IrcView.Nick(group.Key), StringComparer.OrdinalIgnoreCase))
					WhoReply(session, channel.Name, group.First().User,
						IrcView.Prefix(channel, group.First(), now, session.Has("multi-prefix")));
		}
		else if (FindUser(mask) is { } user)
		{
			WhoReply(session, "*", user, string.Empty);
		}

		session.Numeric(IrcNumericReply.RplEndOfWho, mask, "End of /WHO list.");
	}

	private static void WhoReply(IrcSession session, string channel, User user, string prefix)
	{
		var nick = IrcView.Nick(user.Record);
		session.Numeric(IrcNumericReply.RplWhoReply, channel, nick, session.Domain, session.ServerName, nick,
			(user.AwayMessage is null ? "H" : "G") + prefix, $"0 {nick}");
	}

	private void Whois(IrcSession session, Connection actor, IrcMessage message)
	{
		if (message.Parameters.Count == 0)
		{
			session.Numeric(IrcNumericReply.ErrNoNicknameGiven, "No nickname given");
			return;
		}

		var nick = message.Parameters[^1];
		if (FindUser(nick) is not { } user)
		{
			session.Numeric(IrcNumericReply.ErrNoSuchNick, nick, IrcReplies.NoSuchNick);
			session.Numeric(IrcNumericReply.RplEndOfWhoIs, nick, "End of /WHOIS list.");
			return;
		}

		var shown = IrcView.Nick(user.Record);
		session.Numeric(IrcNumericReply.RplWhoIsUser, shown, shown, session.Domain, "*", shown);
		session.Numeric(IrcNumericReply.RplWhoIsServer, shown, session.ServerName, IrcReplies.Network);
		if (user.AwayMessage is { } away) session.Numeric(IrcNumericReply.RplAway, shown, away.ReplaceLineEndings(" "));
		session.Numeric(IrcNumericReply.RplWhoIsAccount, shown, shown, "is logged in as");
		var now = session.Now;
		var visible = VisibleChannels(actor, now).ToHashSet();
		var channels = general.All.Values
			.Where(channel =>
				visible.Contains(channel) && channel.Members.Any(member => ReferenceEquals(member.User, user)))
			.Select(channel => IrcView.Prefix(channel,
				channel.Members.First(member => ReferenceEquals(member.User, user)),
				now, false) + channel.Name).ToList();
		if (channels.Count > 0) session.Numeric(IrcNumericReply.RplWhoIsChannels, shown, string.Join(' ', channels));
		session.Numeric(IrcNumericReply.RplEndOfWhoIs, shown, "End of /WHOIS list.");
	}

	private void Ison(IrcSession session, IrcMessage message)
	{
		var online = message.Parameters
			.SelectMany(parameter => parameter.Split(' ', StringSplitOptions.RemoveEmptyEntries))
			.Select(FindUser).OfType<User>().Select(user => IrcView.Nick(user.Record));
		session.Numeric(IrcNumericReply.RplIson, string.Join(' ', online));
	}

	private void UserHost(IrcSession session, IrcMessage message)
	{
		if (message.Parameters.Count == 0)
		{
			NeedMore(session, "USERHOST");
			return;
		}

		var replies = message.Parameters.Take(5).Select(FindUser).OfType<User>()
			.Select(user =>
				$"{IrcView.Nick(user.Record)}={(user.AwayMessage is null ? '+' : '-')}{IrcView.Nick(user.Record)}@{session.Domain}");
		session.Numeric(IrcNumericReply.RplUserHost, string.Join(' ', replies));
	}
}