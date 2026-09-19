using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Basil.Protocol.Irc;

/// <summary>
///     A single IRC protocol line per RFC 1459 §2.3.1: [":prefix"] command param* [":trailing"].
///     A pure wire-format representation with no server or session semantics.
/// </summary>
/// <param name="Prefix">
///     The optional source prefix, either a server name or a user hostmask; <see langword="null" /> when
///     absent.
/// </param>
/// <param name="Command">The IRC command name or three-digit numeric reply code.</param>
/// <param name="Params">The middle parameters of the line, plus the trailing parameter when present.</param>
public sealed record IrcMessage(string Command, string? Prefix = null, params IReadOnlyList<string> Params)
	: IParsable<IrcMessage>, IFormattable
{
	public string ToString(
		string? format = null,
		IFormatProvider? formatProvider = null)
	{
		var line = Prefix is null ? Command : $":{Prefix} {Command}";

		for (var i = 0; i < Params.Count; i++)
		{
			var param = Params[i];
			var isLast = i == Params.Count - 1;

			line += isLast && (param.Contains(' ') || param.StartsWith(':') || param.Length == 0)
				? $" :{param}"
				: $" {param}";
		}

		return line;
	}

	/// <summary>
	///     Parses a raw IRC line into an <see cref="IrcMessage" />.
	/// </summary>
	/// <param name="s">The raw IRC line to parse, without a trailing CRLF.</param>
	/// <param name="provider">The format provider, which is ignored.</param>
	/// <returns>The parsed IRC message.</returns>
	/// <exception cref="FormatException">The line is empty or malformed.</exception>
	public static IrcMessage Parse(string s, IFormatProvider? provider)
	{
		return TryParse(s, provider, out var message)
			? message
			: throw new FormatException($"Malformed IRC line: \"{s}\"");
	}

	/// <summary>
	///     Tries to parse a raw IRC line into an <see cref="IrcMessage" />.
	/// </summary>
	/// <param name="s">The raw IRC line to parse, without a trailing CRLF.</param>
	/// <param name="provider">The format provider, which is ignored.</param>
	/// <param name="result">The parsed message, or <see langword="null" /> if parsing failed.</param>
	/// <returns><see langword="true" /> if the line parsed successfully; otherwise, <see langword="false" />.</returns>
	public static bool TryParse(
		[NotNullWhen(true)] string? s,
		IFormatProvider? provider,
		[MaybeNullWhen(false)] out IrcMessage result)
	{
		result = null;

		if (string.IsNullOrEmpty(s))
			return false;

		var rest = s;
		string? prefix = null;

		if (rest[0] == ':')
		{
			var spaceIndex = rest.IndexOf(' ');
			if (spaceIndex < 0)
				return false;

			prefix = rest[1..spaceIndex];
			rest = rest[(spaceIndex + 1)..];
		}

		string? trailing = null;

		var colonIndex = rest.IndexOf(" :", StringComparison.Ordinal);
		if (colonIndex >= 0)
		{
			trailing = rest[(colonIndex + 2)..];
			rest = rest[..colonIndex];
		}
		else if (rest.StartsWith(':'))
		{
			trailing = rest[1..];
			rest = string.Empty;
		}

		var tokens = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (tokens.Length == 0)
			return false;

		var parameters = new List<string>(tokens.Length);

		for (var i = 1; i < tokens.Length; i++)
			parameters.Add(tokens[i]);

		if (trailing is not null)
			parameters.Add(trailing);

		result = new IrcMessage(tokens[0], prefix, parameters);
		return true;
	}

	/// <summary>Builds an IRC numeric reply from a numeric reply code.</summary>
	/// <param name="serverName">The name of the server, used as the message prefix.</param>
	/// <param name="numericReply">The numeric reply code.</param>
	/// <param name="target">The target of the reply, usually the nickname of the recipient.</param>
	/// <param name="args">The additional parameters that follow the target.</param>
	/// <returns>The formatted numeric reply message.</returns>
	public static IrcMessage Numeric(
		string serverName, IrcNumericReply numericReply, string target, params string[] args)
	{
		var code = ((int)numericReply).ToString("D3", CultureInfo.InvariantCulture);
		string[] parameters = [target, .. args];

		return new IrcMessage(code, serverName, parameters);
	}

	/// <summary>Builds a PRIVMSG message from a user to a target player or channel.</summary>
	public static IrcMessage Privmsg(IrcUserPrefix senderPrefix, string target, string text)
	{
		return new IrcMessage("PRIVMSG", senderPrefix.ToString(), target, text);
	}

	/// <summary>Builds a NOTICE message from a user to a target player or channel.</summary>
	public static IrcMessage Notice(IrcUserPrefix senderPrefix, string target, string text)
	{
		return new IrcMessage("NOTICE", senderPrefix.ToString(), target, text);
	}

	/// <summary>Builds a JOIN message announcing that a user entered a channel.</summary>
	public static IrcMessage Join(IrcUserPrefix userPrefix, string channel)
	{
		return new IrcMessage("JOIN", userPrefix.ToString(), channel);
	}

	/// <summary>Builds a PART message announcing that a user left a channel.</summary>
	public static IrcMessage Part(IrcUserPrefix userPrefix, string channel, string? reason = null)
	{
		return new IrcMessage("PART", userPrefix.ToString(), reason is null ? [channel] : [channel, reason]);
	}

	/// <summary>Builds a TOPIC message announcing that a channel's topic changed.</summary>
	public static IrcMessage Topic(IrcUserPrefix userPrefix, string channel, string topic)
	{
		return new IrcMessage("TOPIC", userPrefix.ToString(), channel, topic);
	}

	/// <summary>Builds a QUIT message announcing that a user disconnected.</summary>
	public static IrcMessage Quit(IrcUserPrefix userPrefix, string reason)
	{
		return new IrcMessage("QUIT", userPrefix.ToString(), reason);
	}

	/// <summary>Builds a PING message carrying a token for the server to echo back.</summary>
	public static IrcMessage Ping(string token)
	{
		return new IrcMessage("PING", null, token);
	}

	/// <summary>Builds a PONG message echoing back a PING token.</summary>
	public static IrcMessage Pong(string token)
	{
		return new IrcMessage("PONG", null, token);
	}

	public override string ToString()
	{
		return ToString();
	}
}