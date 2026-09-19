using System.Diagnostics.CodeAnalysis;

namespace Basil.Protocol.Irc;

/// <summary>Represents an IRC user prefix.</summary>
/// <remarks>
///     The prefix has one of the forms <c>nick</c>, <c>nick@host</c>, or <c>nick!user@host</c>.
/// </remarks>
public readonly record struct IrcUserPrefix(string Nick, string? User = null, string? Host = null)
	: IParsable<IrcUserPrefix>, IFormattable
{
	public string ToString(
		string? format = null,
		IFormatProvider? formatProvider = null)
	{
		if (User is not null)
			return Host is not null
				? $"{Nick}!{User}@{Host}"
				: throw new InvalidOperationException("An IRC user prefix with a user must have a host.");

		return Host is not null ? $"{Nick}@{Host}" : Nick;
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

		switch (bang)
		{
			// nick
			case < 0 when at < 0:
				result = new IrcUserPrefix(s);
				return true;
			// nick@host
			case < 0 when at <= 0 || at == s.Length - 1:
				return false;
			case < 0:
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
		return ToString();
	}
}