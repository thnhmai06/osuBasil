using System.Diagnostics.CodeAnalysis;

namespace Basil.Protocol.Irc.Models;

public readonly record struct IrcUserPrefix : IParsable<IrcUserPrefix>, IFormattable
{
	public IrcUserPrefix(string nick, string? user = null, string? host = null)
	{
		if (user is not null && host is null)
			throw new ArgumentException(
				"An IRC user prefix with a user must have a host.",
				nameof(host));

		Nick = nick;
		User = user;
		Host = host;
	}

	public string Nick { get; }

	public string? User { get; }

	public string? Host { get; }

	public string ToString(string? format = null, IFormatProvider? formatProvider = null)
	{
		return Format();
	}

	public static IrcUserPrefix Parse(string s, IFormatProvider? provider)
	{
		return TryParse(s, provider, out var result)
			? result
			: throw new FormatException($"Malformed IRC user prefix: \"{s}\"");
	}

	public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out IrcUserPrefix result)
	{
		result = default;

		if (string.IsNullOrEmpty(s))
			return false;

		var bang = s.IndexOf('!');
		var at = s.IndexOf('@');

		// nick
		if (bang < 0 && at < 0)
		{
			result = new IrcUserPrefix(s);
			return true;
		}

		// nick@host
		if (bang < 0)
		{
			if (at <= 0 || at == s.Length - 1)
				return false;

			result = new IrcUserPrefix(s[..at], null, s[(at + 1)..]);
			return true;
		}

		// nick!user@host
		if (at <= bang + 1 || bang <= 0 || at == s.Length - 1)
			return false;

		result = new IrcUserPrefix(s[..bang], s[(bang + 1)..at], s[(at + 1)..]);
		return true;
	}

	public override string ToString()
	{
		return Format();
	}

	private string Format()
	{
		if (User is not null)
			return $"{Nick}!{User}@{Host}";

		return Host is not null ? $"{Nick}@{Host}" : Nick;
	}
}