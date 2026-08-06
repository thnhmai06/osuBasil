using System.Globalization;

namespace Basil.Protocol.Irc;

/// <summary>Formats <see cref="IrcMessage" />s (or the common shapes Basil's IRC bridge sends) into raw wire lines.</summary>
public static class IrcMessageWriter
{
	/// <summary>Formats an <see cref="IrcMessage" /> into a single raw IRC line, without a trailing CRLF.</summary>
	/// <param name="message">The message to serialize.</param>
	/// <returns>The raw IRC line for the message.</returns>
	public static string Format(IrcMessage message)
	{
		var line = message.Prefix is null ? message.Command : $":{message.Prefix} {message.Command}";

		for (var i = 0; i < message.Params.Count; i++)
		{
			var param = message.Params[i];
			var isLast = i == message.Params.Count - 1;
			line += isLast && (param.Contains(' ') || param.StartsWith(':') || param.Length == 0)
				? $" :{param}"
				: $" {param}";
		}

		return line;
	}

	/// <summary>
	///     Builds a user-hostmask prefix ("nick!id@host") for JOIN/PART/QUIT/PRIVMSG originating from a user.
	///     The "user" slot carries the sender's <c>UserSession</c> id (not a real ident) so
	///     <c>BanchoIrcBridgeConnection</c> can recover it without a session-registry lookup; a real IRC client
	///     just displays it as an ordinary hostmask.
	/// </summary>
	public static string UserPrefix(string nick, int id)
	{
		return $"{nick}!{id}@basil";
	}

	/// <summary>
	///     Splits a <see cref="UserPrefix" /> back into (nick, id). Returns false for a client-sent (prefix-less)
	///     message.
	/// </summary>
	public static bool TryParseUserPrefix(string? prefix, out string nick, out int id)
	{
		nick = "";
		id = 0;
		if (prefix is null) return false;

		var bang = prefix.IndexOf('!');
		var at = prefix.IndexOf('@');
		if (bang < 0 || at < bang) return false;

		nick = prefix[..bang];
		return int.TryParse(prefix[(bang + 1)..at], out id);
	}

	/// <summary>Builds an IRC numeric reply from a three-digit reply code.</summary>
	/// <param name="serverName">The name of the server, used as the message prefix.</param>
	/// <param name="numeric">One of the enumeration values that specifies the numeric reply code.</param>
	/// <param name="target">The target of the reply, usually the nickname of the recipient.</param>
	/// <param name="args">The additional parameters that follow the target.</param>
	/// <returns>The formatted numeric reply message.</returns>
	public static IrcMessage Numeric(string serverName, IrcNumeric numeric, string target, params string[] args)
	{
		var code = ((int)numeric).ToString("D3", CultureInfo.InvariantCulture);
		var parameters = new List<string> { target };
		parameters.AddRange(args);
		return new IrcMessage(serverName, code, parameters);
	}

	/// <summary>Builds a PRIVMSG message from a user to a target player or channel.</summary>
	/// <param name="senderNick">The nickname of the sending user.</param>
	/// <param name="senderId">The <c>UserSession</c> id of the sending user, embedded in the hostmask.</param>
	/// <param name="target">The nickname or channel the message is sent to.</param>
	/// <param name="text">The message body.</param>
	/// <returns>The PRIVMSG message.</returns>
	public static IrcMessage Privmsg(string senderNick, int senderId, string target, string text)
	{
		return new IrcMessage(UserPrefix(senderNick, senderId), "PRIVMSG", [target, text]);
	}

	/// <summary>Builds a JOIN message announcing that a user entered a channel.</summary>
	/// <param name="nick">The nickname of the joining user.</param>
	/// <param name="id">The <c>UserSession</c> id of the joining user, embedded in the hostmask.</param>
	/// <param name="channel">The name of the channel joined.</param>
	/// <returns>The JOIN message.</returns>
	public static IrcMessage Join(string nick, int id, string channel)
	{
		return new IrcMessage(UserPrefix(nick, id), "JOIN", [channel]);
	}

	/// <summary>Builds a PART message announcing that a user left a channel, optionally with a reason.</summary>
	/// <param name="nick">The nickname of the leaving user.</param>
	/// <param name="id">The <c>UserSession</c> id of the leaving user, embedded in the hostmask.</param>
	/// <param name="channel">The name of the channel left.</param>
	/// <param name="reason">The optional leave reason appended as the trailing parameter.</param>
	/// <returns>The PART message.</returns>
	public static IrcMessage Part(string nick, int id, string channel, string? reason = null)
	{
		var parameters = reason is null ? new List<string> { channel } : [channel, reason];
		return new IrcMessage(UserPrefix(nick, id), "PART", parameters);
	}

	/// <summary>Builds a QUIT message announcing that a user disconnected.</summary>
	/// <param name="nick">The nickname of the disconnecting user.</param>
	/// <param name="id">The <c>UserSession</c> id of the disconnecting user, embedded in the hostmask.</param>
	/// <param name="reason">The quit message shown to other users.</param>
	/// <returns>The QUIT message.</returns>
	public static IrcMessage Quit(string nick, int id, string reason)
	{
		return new IrcMessage(UserPrefix(nick, id), "QUIT", [reason]);
	}

	/// <summary>Builds a PING message carrying a token for the server to echo back.</summary>
	/// <param name="token">The token to echo back in the PONG reply.</param>
	/// <returns>The PING message.</returns>
	public static IrcMessage Ping(string token)
	{
		return new IrcMessage(null, "PING", [token]);
	}

	/// <summary>Builds a PONG message echoing back a PING token.</summary>
	/// <param name="token">The token received in the PING message.</param>
	/// <returns>The PONG message.</returns>
	public static IrcMessage Pong(string token)
	{
		return new IrcMessage(null, "PONG", [token]);
	}
}