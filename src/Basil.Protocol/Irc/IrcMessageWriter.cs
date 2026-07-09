using System.Globalization;

namespace Basil.Protocol.Irc;

/// <summary>Formats <see cref="IrcMessage" />s (or the common shapes Basil's IRC bridge sends) into raw wire lines.</summary>
public static class IrcMessageWriter
{
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
    ///     The "user" slot carries the sender's <see cref="Basil.Application" /> player id (not a real ident) so
    ///     <c>BanchoIrcBridgeConnection</c> can recover it without a session-registry lookup — a real IRC client
    ///     just displays it as an ordinary hostmask.
    /// </summary>
    public static string UserPrefix(string nick, int id)
    {
        return $"{nick}!{id}@basil";
    }

    /// <summary>Splits a <see cref="UserPrefix" /> back into (nick, id). Returns false for a client-sent (prefix-less) message.</summary>
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

    public static IrcMessage Numeric(string serverName, IrcNumeric numeric, string target, params string[] args)
    {
        var code = ((int)numeric).ToString("D3", CultureInfo.InvariantCulture);
        var parameters = new List<string> { target };
        parameters.AddRange(args);
        return new IrcMessage(serverName, code, parameters);
    }

    public static IrcMessage Privmsg(string senderNick, int senderId, string target, string text)
    {
        return new IrcMessage(UserPrefix(senderNick, senderId), "PRIVMSG", [target, text]);
    }

    public static IrcMessage Join(string nick, int id, string channel)
    {
        return new IrcMessage(UserPrefix(nick, id), "JOIN", [channel]);
    }

    public static IrcMessage Part(string nick, int id, string channel, string? reason = null)
    {
        var parameters = reason is null ? new List<string> { channel } : [channel, reason];
        return new IrcMessage(UserPrefix(nick, id), "PART", parameters);
    }

    public static IrcMessage Quit(string nick, int id, string reason)
    {
        return new IrcMessage(UserPrefix(nick, id), "QUIT", [reason]);
    }

    public static IrcMessage Ping(string token)
    {
        return new IrcMessage(null, "PING", [token]);
    }

    public static IrcMessage Pong(string token)
    {
        return new IrcMessage(null, "PONG", [token]);
    }
}
