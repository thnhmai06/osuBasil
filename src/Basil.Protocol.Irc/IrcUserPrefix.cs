using System.Diagnostics.CodeAnalysis;

namespace Basil.Protocol.Irc;

/// <summary>Represents an IRC user prefix.</summary>
/// <remarks>
///     The prefix has one of the forms <c>nick</c>, <c>nick@host</c>, or <c>nick!user@host</c>.
/// </remarks>
public readonly record struct IrcUserPrefix(string Nick, string? User = null, string? Host = null)
	: IParsable<IrcUserPrefix>, IFormattable
{
	/// <summary>
	///     Formats the prefix in its wire form: <c>nick</c>, <c>nick@host</c>, or
	///     <c>nick!user@host</c>.
	/// </summary>
	/// <param name="format">Ignored.</param>
	/// <param name="formatProvider">Ignored.</param>
	/// <returns>The formatted prefix.</returns>
	/// <exception cref="InvalidOperationException">
	///     The prefix has a user but no host, a combination that cannot be represented.
	/// </exception>
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

	/// <summary>
	///     Parses a wire-format prefix into an <see cref="IrcUserPrefix" />.
	/// </summary>
	/// <param name="s">
	///     The prefix string, one of <c>nick</c>, <c>nick@host</c>, or <c>nick!user@host</c>.
	/// </param>
	/// <param name="provider">The format provider, which is ignored.</param>
	/// <returns>The parsed prefix.</returns>
	/// <exception cref="FormatException">The string is not a valid prefix.</exception>
	public static IrcUserPrefix Parse(string s, IFormatProvider? provider)
	{
		return TryParse(s, provider, out var result)
			? result
			: throw new FormatException($"Malformed IRC user prefix: \"{s}\"");
	}

	/// <summary>
	///     Tries to parse a wire-format prefix into an <see cref="IrcUserPrefix" />.
	/// </summary>
	/// <param name="s">
	///     The prefix string, one of <c>nick</c>, <c>nick@host</c>, or <c>nick!user@host</c>.
	/// </param>
	/// <param name="provider">The format provider, which is ignored.</param>
	/// <param name="result">The parsed prefix on success; the default value on failure.</param>
	/// <returns>
	///     <see langword="true" /> if the string parsed successfully; otherwise,
	///     <see langword="false" />.
	/// </returns>
	/// <remarks>
	///     Parsing fails on a null or empty string, on a user part without a host, and on a prefix
	///     with an empty nick, user, or host part.
	/// </remarks>
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

	/// <summary>
	///     Returns the prefix in its wire form: <c>nick</c>, <c>nick@host</c>, or
	///     <c>nick!user@host</c>.
	/// </summary>
	/// <returns>
	///     The formatted prefix, exactly as produced by
	///     <see cref="ToString(string?, IFormatProvider?)" />.
	/// </returns>
	public override string ToString()
	{
		return ToString();
	}
}