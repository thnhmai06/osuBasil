using Basil.Application.Chat.Objects;
using Basil.Application.Users.Objects.Sessions;
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

	public static string Prefix(Channel channel, Session member, DateTimeOffset now, bool all)
	{
		var privileges = member.EffectivePrivileges(now);
		var (authority, voice) = channel switch
		{
			RoomChannel room => (room.Room.Authority.IsManagedBy(member.Record),
				room.Room.Involves(member, privileges) || privileges.Allows(UserPrivileges.Tournament)),
			GeneralChannel general => (privileges.Allows(UserPrivileges.Moderator),
				privileges.Allows(general.Record.Value.WritePrivileges)),
			_ => (false, false)
		};
		var symbols = (authority ? "@" : string.Empty) + (voice ? "+" : string.Empty);
		return all || symbols.Length == 0 ? symbols : symbols[..1];
	}

	public static int UserCount(Channel channel)
	{
		return channel.Members.Select(member => member.Record).Distinct().Count();
	}
}