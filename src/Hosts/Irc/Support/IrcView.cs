using Basil.Application.Chat.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;
using Basil.Protocol.Irc;

namespace Basil.Host.Irc.Support;

/// <summary>Translates the server's objects into what IRC clients show.</summary>
internal static class IrcView
{
	/// <summary>Gets the nickname of an account: its username with spaces replaced by underscores.</summary>
	/// <param name="user">The account.</param>
	/// <returns>The nickname.</returns>
	public static string Nick(UserRecord user)
	{
		return user.Value.Name.Replace(' ', '_');
	}

	/// <summary>Checks whether a nickname a client wrote names an account, folding ASCII case.</summary>
	/// <param name="user">The account.</param>
	/// <param name="nick">The nickname the client wrote.</param>
	/// <returns><see langword="true" /> when the nickname is the account's.</returns>
	public static bool IsNick(UserRecord user, string nick)
	{
		return string.Equals(Nick(user), nick, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>Gets the fixed mask under which every user appears; it never holds a real address.</summary>
	/// <param name="user">The account.</param>
	/// <param name="domain">The public domain.</param>
	/// <returns>The <c>nick!nick@domain</c> prefix.</returns>
	public static IrcUserPrefix Mask(UserRecord user, string domain)
	{
		return new IrcUserPrefix(Nick(user), Nick(user), domain);
	}

	/// <summary>Gets the status symbols of a user in a channel.</summary>
	/// <param name="channel">The channel.</param>
	/// <param name="member">One of the user's connections in the channel.</param>
	/// <param name="now">The current time, for the effective permissions.</param>
	/// <param name="all">Whether to list every symbol (multi-prefix) instead of the highest.</param>
	/// <returns>The symbols, <c>@</c> before <c>+</c>; empty for a read-only member.</returns>
	public static string Prefix(Channel channel, Connection member, DateTimeOffset now, bool all)
	{
		var permissions = member.EffectivePermissions(now);
		var (authority, voice) = channel switch
		{
			RoomChannel room => (room.Room.Authority.IsManagedBy(member.Record),
				room.Room.Involves(member, now) || permissions.Allows(UserPermissions.ChatManagePostAnyRoom)),
			GeneralChannel general => (permissions.Allows(UserPermissions.ChatManageChannels),
				permissions.Privileges.Allows(general.Record.Value.WritePrivileges)),
			_ => (false, false)
		};
		var symbols = (authority ? "@" : string.Empty) + (voice ? "+" : string.Empty);
		return all || symbols.Length == 0 ? symbols : symbols[..1];
	}

	/// <summary>Counts the distinct users in a channel.</summary>
	/// <param name="channel">The channel.</param>
	/// <returns>How many accounts have a connection in it.</returns>
	public static int UserCount(Channel channel)
	{
		return channel.Members.Select(member => member.Record).Distinct().Count();
	}
}