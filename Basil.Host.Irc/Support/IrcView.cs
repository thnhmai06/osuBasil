using Basil.Application.Chat.Objects;
using Basil.Domain.Users;
using Basil.Protocol.Irc.Models;

namespace Basil.Host.Irc.Support;

internal static class IrcView
{
	public static string Nick(UserRecord user)
	{
		return user.Value.Name.Replace(' ', '_');
	}

	public static bool IsNick(UserRecord user, string nick)
	{
		return string.Equals(Nick(user), nick, StringComparison.OrdinalIgnoreCase);
	}

	public static IrcUserPrefix Mask(UserRecord user, string domain)
	{
		return new IrcUserPrefix(Nick(user), Nick(user), domain);
	}

	public static string Prefix(ChannelMemberRole role, bool all)
	{
		var symbols = (role.HasFlag(ChannelMemberRole.Manager) ? "@" : string.Empty) +
		              (role.HasFlag(ChannelMemberRole.Speaker) ? "+" : string.Empty);
		return all || symbols.Length == 0 ? symbols : symbols[..1];
	}

	public static int UserCount(Channel channel)
	{
		return channel.Members.Select(member => member.Record).Distinct().Count();
	}
}