using System.Diagnostics.CodeAnalysis;

namespace Basil.Protocol.Irc;

/// <summary>Represents an IRC user prefix.</summary>
/// <remarks>
///     The prefix has one of the forms <c>nick</c>, <c>nick@host</c>, or <c>nick!user@host</c>.
/// </remarks>
public readonly record struct IrcUserPrefix
	: IParsable<IrcUserPrefix>, IFormattable
{
	/// <summary>Creates a prefix from its parts.</summary>
	/// <param name="nick">The user's nickname.</param>
	/// <param name="user">The user name, or <see langword="null" /> when the prefix omits it.</param>
	/// <param name="host">The host, or <see langword="null" /> when the prefix omits it.</param>
	/// <exception cref="ArgumentException">
	///     <paramref name="user" /> is set while <paramref name="host" /> is <see langword="null" />, a
	///     combination that cannot be represented.
	/// </exception>
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

	/// <summary>The user's nickname.</summary>
	public string Nick { get; }

	/// <summary>The user name, or <see langword="null" /> when the prefix omits it.</summary>
	public string? User { get; }

	/// <summary>The host, or <see langword="null" /> when the prefix omits it.</summary>
	public string? Host { get; }

	/// <summary>
	///     Formats the prefix in its wire form: <c>nick</c>, <c>nick@host</c>, or
	///     <c>nick!user@host</c>.
	/// </summary>
	/// <param name="format">Ignored.</param>
	/// <param name="formatProvider">Ignored.</param>
	/// <returns>The formatted prefix.</returns>
	public string ToString(string? format = null, IFormatProvider? formatProvider = null)
	{
		return Format();
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
		return Format();
	}

	private string Format()
	{
		if (User is not null)
			return $"{Nick}!{User}@{Host}";

		return Host is not null ? $"{Nick}@{Host}" : Nick;
	}
}