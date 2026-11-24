using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Basil.Protocol.Irc.Models;

public sealed record IrcMessage(string Command, string? Prefix = null, params IReadOnlyList<string> Parameters)
	: IParsable<IrcMessage>, IFormattable
{
	public IReadOnlyList<IrcTag> Tags { get; init; } = [];

	public bool ForceTrailing { get; init; }

	public bool Equals(IrcMessage? other)
	{
		return other is not null
		       && string.Equals(Command, other.Command, StringComparison.Ordinal)
		       && string.Equals(Prefix, other.Prefix, StringComparison.Ordinal)
		       && Tags.SequenceEqual(other.Tags)
		       && Parameters.SequenceEqual(other.Parameters, StringComparer.Ordinal);
	}

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

			line += isLast && (ForceTrailing || parameter.Contains(' ') || parameter.StartsWith(':') ||
			                   parameter.Length == 0)
				? $" :{parameter}"
				: $" {parameter}";
		}

		return line;
	}

	public static IrcMessage Parse(string s, IFormatProvider? provider)
	{
		return TryParse(s, provider, out var message)
			? message
			: throw new FormatException($"Malformed IRC line: \"{s}\"");
	}

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

	public string? Tag(string key)
	{
		return Tags.FirstOrDefault(tag => tag.Key == key) is { } found ? found.Value ?? string.Empty : null;
	}

	public IrcMessage WithTag(string key, string? value = null)
	{
		return this with { Tags = [.. Tags, new IrcTag(key, value)] };
	}

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

	private static void EnsureSingleLine(string value, string paramName)
	{
		if (value.AsSpan().IndexOfAny('\r', '\n', '\0') >= 0)
			throw new ArgumentException(
				"An IRC line may not contain a carriage return, a line feed or a null character.",
				paramName);
	}

	public static IrcMessage Numeric(
		string serverName, IrcNumericReply numericReply, string target, params string[] args)
	{
		var code = ((int)numericReply).ToString("D3", CultureInfo.InvariantCulture);
		string[] parameters = [target, .. args];

		return new IrcMessage(code, serverName, parameters);
	}

	public static IrcMessage Privmsg(IrcUserPrefix senderPrefix, string target, string text)
	{
		return new IrcMessage("PRIVMSG", senderPrefix.ToString(), target, text);
	}

	public static IrcMessage Notice(IrcUserPrefix senderPrefix, string target, string text)
	{
		return new IrcMessage("NOTICE", senderPrefix.ToString(), target, text);
	}

	public static IrcMessage Join(IrcUserPrefix userPrefix, string channel)
	{
		return new IrcMessage("JOIN", userPrefix.ToString(), channel);
	}

	public static IrcMessage Part(IrcUserPrefix userPrefix, string channel, string? reason = null)
	{
		return new IrcMessage("PART", userPrefix.ToString(), reason is null ? [channel] : [channel, reason]);
	}

	public static IrcMessage Topic(IrcUserPrefix userPrefix, string channel, string topic)
	{
		return new IrcMessage("TOPIC", userPrefix.ToString(), channel, topic);
	}

	public static IrcMessage Quit(IrcUserPrefix userPrefix, string reason)
	{
		return new IrcMessage("QUIT", userPrefix.ToString(), reason);
	}

	public static IrcMessage Ping(string token)
	{
		return new IrcMessage("PING", null, token);
	}

	public static IrcMessage Pong(string token)
	{
		return new IrcMessage("PONG", null, token);
	}

	public static IrcMessage Join(IrcUserPrefix userPrefix, string channel, string account, string realName)
	{
		return new IrcMessage(IrcVerb.Join, userPrefix.ToString(), channel, account, realName);
	}

	public static IrcMessage Kick(string serverName, string channel, string target, string reason)
	{
		return new IrcMessage(IrcVerb.Kick, serverName, channel, target, reason);
	}

	public static IrcMessage NickChange(string oldNick, string newNick)
	{
		return new IrcMessage(IrcVerb.Nick, oldNick, newNick);
	}

	public static IrcMessage Away(IrcUserPrefix userPrefix, string? text)
	{
		return new IrcMessage(IrcVerb.Away, userPrefix.ToString(), text is null ? [] : [text]) { ForceTrailing = true };
	}

	public static IrcMessage Error(string text)
	{
		return new IrcMessage(IrcVerb.Error, null, text);
	}

	public static IrcMessage Cap(string serverName, string target, string subcommand, string capabilities)
	{
		return new IrcMessage(IrcVerb.Cap, serverName, target, subcommand, capabilities) { ForceTrailing = true };
	}

	public static IrcMessage Authenticate(string data)
	{
		return new IrcMessage(IrcVerb.Authenticate, null, data);
	}

	public static IrcMessage Fail(string serverName, string command, string code, string context, string description)
	{
		return new IrcMessage(IrcVerb.Fail, serverName, command, code, context, description);
	}

	public static IrcMessage ServerNotice(string serverName, string target, string text)
	{
		return new IrcMessage(IrcVerb.Notice, serverName, target, text);
	}

	public static IrcMessage ServerPing(string serverName)
	{
		return new IrcMessage(IrcVerb.Ping, null, serverName) { ForceTrailing = true };
	}

	public static IrcMessage ServerPong(string serverName, string token)
	{
		return new IrcMessage(IrcVerb.Pong, serverName, serverName, token) { ForceTrailing = true };
	}

	public override string ToString()
	{
		return ToString();
	}
}