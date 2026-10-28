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
/// <param name="Parameters">The middle parameters of the line, plus the trailing parameter when present.</param>
public sealed record IrcMessage(string Command, string? Prefix = null, params IReadOnlyList<string> Parameters)
	: IParsable<IrcMessage>, IFormattable
{
	/// <summary>Gets the IRCv3 message tags in wire order; empty when the line carries none.</summary>
	public IReadOnlyList<IrcTag> Tags { get; init; } = [];

	/// <summary>
	///     Gets a value that indicates whether the last parameter is always written as a trailing parameter (with a
	///     colon), as servers write the text of a chat message even when it has no space.
	/// </summary>
	/// <remarks>Only the written form changes; two messages that differ only here are equal.</remarks>
	public bool ForceTrailing { get; init; }

	/// <summary>Finds the value of a message tag.</summary>
	/// <param name="key">The tag key, including a leading <c>+</c> for a client-only tag.</param>
	/// <returns>The tag's value, an empty string for a tag without a value, or <see langword="null" /> when absent.</returns>
	public string? Tag(string key)
	{
		return Tags.FirstOrDefault(tag => tag.Key == key) is { } found ? found.Value ?? string.Empty : null;
	}

	/// <summary>Returns a copy of this message that carries one more tag.</summary>
	/// <param name="key">The tag key.</param>
	/// <param name="value">The tag value, or <see langword="null" /> for a tag without a value.</param>
	/// <returns>The tagged message.</returns>
	public IrcMessage WithTag(string key, string? value = null)
	{
		return this with { Tags = [.. Tags, new IrcTag(key, value)] };
	}

	/// <summary>
	///     Determines whether this message has the same content as another message.
	/// </summary>
	/// <param name="other">The message to compare with, or <see langword="null" />.</param>
	/// <returns>
	///     <see langword="true" /> when the command, the prefix and every parameter are equal;
	///     otherwise, <see langword="false" />.
	/// </returns>
	public bool Equals(IrcMessage? other)
	{
		return other is not null
		       && string.Equals(Command, other.Command, StringComparison.Ordinal)
		       && string.Equals(Prefix, other.Prefix, StringComparison.Ordinal)
		       && Tags.SequenceEqual(other.Tags)
		       && Parameters.SequenceEqual(other.Parameters, StringComparer.Ordinal);
	}

	/// <summary>
	///     Formats the message as a single IRC wire line.
	/// </summary>
	/// <param name="format">Ignored.</param>
	/// <param name="formatProvider">Ignored.</param>
	/// <returns>
	///     The wire line: the tags (prefixed with '@') when present, then the prefix (prefixed with ':') when
	///     present, then the command, then the parameters. The final parameter is prefixed with " :" when it contains a space, starts
	///     with a colon, or is empty; all other parameters are emitted as-is.
	/// </returns>
	/// <exception cref="ArgumentException">
	///     The command, the prefix or a parameter contains a carriage return, a line feed or a null
	///     character, or a parameter that is not the last one is empty, contains a space or starts
	///     with a colon.
	/// </exception>
	public string ToString(
		string? format = null,
		IFormatProvider? formatProvider = null)
	{
		EnsureSingleLine(Command, nameof(Command));

		if (Prefix is not null)
			EnsureSingleLine(Prefix, nameof(Prefix));

		for (var i = 0; i < Parameters.Count; i++)
		{
			var parameter = Parameters[i];

			EnsureSingleLine(parameter, nameof(Parameters));

			if (i < Parameters.Count - 1
			    && (parameter.Length == 0 || parameter.Contains(' ') || parameter.StartsWith(':')))
				throw new ArgumentException(
					"A parameter that is not the last one must be non-empty, must not contain a space and must not start with a colon.");
		}

		var line = Prefix is null ? Command : $":{Prefix} {Command}";
		if (Tags.Count > 0)
			line = $"@{string.Join(';', Tags.Select(tag => tag.ToString()))} {line}";

		for (var i = 0; i < Parameters.Count; i++)
		{
			var parameter = Parameters[i];
			var isLast = i == Parameters.Count - 1;

			line += isLast && (ForceTrailing || parameter.Contains(' ') || parameter.StartsWith(':') || parameter.Length == 0)
				? $" :{parameter}"
				: $" {parameter}";
		}

		return line;
	}

	/// <summary>
	///     Parses a raw IRC line into an <see cref="IrcMessage" />.
	/// </summary>
	/// <param name="s">The raw IRC line to parse, without a trailing CRLF.</param>
	/// <param name="provider">The format provider, which is ignored.</param>
	/// <returns>The parsed IRC message.</returns>
	/// <remarks>
	///     A line that contains a carriage return, a line feed or a null character is rejected, and a
	///     prefix that is present but empty is rejected. A leading IRCv3 tag section is read into
	///     <see cref="Tags" />, and the command is upper-cased with the invariant culture. Keeping a line within the protocol's
	///     length limit is the caller's job.
	/// </remarks>
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
	/// <remarks>
	///     A line that contains a carriage return, a line feed or a null character is rejected, and a
	///     prefix that is present but empty is rejected. A leading IRCv3 tag section is read into
	///     <see cref="Tags" />, and the command is upper-cased with the invariant culture. Keeping a line within the protocol's
	///     length limit is the caller's job.
	/// </remarks>
	public static bool TryParse(
		[NotNullWhen(true)] string? s,
		IFormatProvider? provider,
		[MaybeNullWhen(false)] out IrcMessage result)
	{
		result = null;

		if (string.IsNullOrEmpty(s))
			return false;

		if (s.AsSpan().IndexOfAny('\r', '\n', '\0') >= 0)
			return false;

		var rest = s;
		IrcTag[] tags = [];

		if (rest[0] == '@')
		{
			var tagEnd = rest.IndexOf(' ');
			if (tagEnd < 0)
				return false;

			tags = [.. rest[1..tagEnd].Split(';', StringSplitOptions.RemoveEmptyEntries).Select(IrcTag.Parse)];
			rest = rest[(tagEnd + 1)..].TrimStart(' ');
		}

		if (rest.Length == 0)
			return false;

		string? prefix = null;

		if (rest[0] == ':')
		{
			var spaceIndex = rest.IndexOf(' ');
			if (spaceIndex <= 1)
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

		result = new IrcMessage(tokens[0].ToUpperInvariant(), prefix, parameters) { Tags = tags };
		return true;
	}

	/// <summary>
	///     Returns a hash code consistent with <see cref="Equals(IrcMessage?)" />.
	/// </summary>
	/// <returns>A hash code built from the command, the prefix and the parameters.</returns>
	public override int GetHashCode()
	{
		var hash = new HashCode();
		hash.Add(Command, StringComparer.Ordinal);
		hash.Add(Prefix, StringComparer.Ordinal);

		foreach (var tag in Tags)
			hash.Add(tag);

		foreach (var parameter in Parameters)
			hash.Add(parameter, StringComparer.Ordinal);

		return hash.ToHashCode();
	}

	/// <summary>
	///     Throws when a part of the line would split it into more than one line.
	/// </summary>
	private static void EnsureSingleLine(string value, string paramName)
	{
		if (value.AsSpan().IndexOfAny('\r', '\n', '\0') >= 0)
			throw new ArgumentException(
				"An IRC line may not contain a carriage return, a line feed or a null character.",
				paramName);
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
	/// <param name="senderPrefix">The prefix of the sending user, formatted into the message prefix.</param>
	/// <param name="target">The nick or channel receiving the message.</param>
	/// <param name="text">The message text, sent as the trailing parameter.</param>
	/// <returns>The PRIVMSG message.</returns>
	public static IrcMessage Privmsg(IrcUserPrefix senderPrefix, string target, string text)
	{
		return new IrcMessage("PRIVMSG", senderPrefix.ToString(), target, text);
	}

	/// <summary>Builds a NOTICE message from a user to a target player or channel.</summary>
	/// <param name="senderPrefix">The prefix of the sending user, formatted into the message prefix.</param>
	/// <param name="target">The nick or channel receiving the message.</param>
	/// <param name="text">The message text, sent as the trailing parameter.</param>
	/// <returns>The NOTICE message.</returns>
	public static IrcMessage Notice(IrcUserPrefix senderPrefix, string target, string text)
	{
		return new IrcMessage("NOTICE", senderPrefix.ToString(), target, text);
	}

	/// <summary>Builds a JOIN message announcing that a user entered a channel.</summary>
	/// <param name="userPrefix">The prefix of the joining user, formatted into the message prefix.</param>
	/// <param name="channel">The joined channel.</param>
	/// <returns>The JOIN message.</returns>
	public static IrcMessage Join(IrcUserPrefix userPrefix, string channel)
	{
		return new IrcMessage("JOIN", userPrefix.ToString(), channel);
	}

	/// <summary>Builds a PART message announcing that a user left a channel.</summary>
	/// <param name="userPrefix">The prefix of the leaving user, formatted into the message prefix.</param>
	/// <param name="channel">The left channel.</param>
	/// <param name="reason">
	///     The optional departure reason, sent as the trailing parameter; <see langword="null" /> to
	///     omit it.
	/// </param>
	/// <returns>The PART message.</returns>
	public static IrcMessage Part(IrcUserPrefix userPrefix, string channel, string? reason = null)
	{
		return new IrcMessage("PART", userPrefix.ToString(), reason is null ? [channel] : [channel, reason]);
	}

	/// <summary>Builds a TOPIC message announcing that a channel's topic changed.</summary>
	/// <param name="userPrefix">The prefix of the user who changed the topic, formatted into the message prefix.</param>
	/// <param name="channel">The channel whose topic changed.</param>
	/// <param name="topic">The new topic text.</param>
	/// <returns>The TOPIC message.</returns>
	public static IrcMessage Topic(IrcUserPrefix userPrefix, string channel, string topic)
	{
		return new IrcMessage("TOPIC", userPrefix.ToString(), channel, topic);
	}

	/// <summary>Builds a QUIT message announcing that a user disconnected.</summary>
	/// <param name="userPrefix">The prefix of the disconnecting user, formatted into the message prefix.</param>
	/// <param name="reason">The disconnect reason, sent as the trailing parameter.</param>
	/// <returns>The QUIT message.</returns>
	public static IrcMessage Quit(IrcUserPrefix userPrefix, string reason)
	{
		return new IrcMessage("QUIT", userPrefix.ToString(), reason);
	}

	/// <summary>Builds a PING message carrying a token for the server to echo back.</summary>
	/// <param name="token">The token to echo back in the matching PONG.</param>
	/// <returns>The PING message.</returns>
	public static IrcMessage Ping(string token)
	{
		return new IrcMessage("PING", null, token);
	}

	/// <summary>Builds a PONG message echoing back a PING token.</summary>
	/// <param name="token">The token received in the matching PING.</param>
	/// <returns>The PONG message.</returns>
	public static IrcMessage Pong(string token)
	{
		return new IrcMessage("PONG", null, token);
	}

	/// <summary>
	///     Returns the message serialized as a single IRC wire line.
	/// </summary>
	/// <returns>
	///     The message in wire format: the prefix (when present), the command, and the parameters,
	///     exactly as produced by <see cref="ToString(string?, IFormatProvider?)" />.
	/// </returns>
	public override string ToString()
	{
		return ToString();
	}
}