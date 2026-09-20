using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Basil.Domain.Client;
using Basil.Domain.Users;

namespace Basil.Application.Queries;

/// <summary>
///     Parses <c>GET /users/search</c>'s query syntax (<c>key&lt;operator&gt;value</c> tokens mixed
///     with a free-text id/username portion, e.g. <c>peppy country=jp</c>) into a structured
///     <see cref="UserQuery" />.
/// </summary>
public partial record UserQuery
{
	/// <summary>
	///     Parses a user search query string into a <see cref="UserQuery" />.
	/// </summary>
	/// <param name="query">The raw query text; blank or whitespace-only text yields <see cref="Empty" />.</param>
	/// <param name="provider">The format provider, which is ignored.</param>
	/// <returns>The structured query parsed from <paramref name="query" />.</returns>
	/// <remarks>
	///     Recognized <c>key:value</c> and <c>key=value</c> tokens (country, privilege) are applied
	///     as filters; unrecognized tokens remain part of the free-text search. The provider does
	///     not affect parsing.
	/// </remarks>
	public static UserQuery Parse(string query, IFormatProvider? provider)
	{
		if (string.IsNullOrWhiteSpace(query))
			return Empty;

		var builder = new Builder();

		var keywords = TokenPattern().Replace(query, match =>
		{
			var key = match.Groups["key"].Value.ToLowerInvariant();
			var rawValue = Unquote(match.Groups["value"].Value);

			return builder.TryApply(key, rawValue)
				? ""
				: match.Value;
		});

		return builder.Build(CollapseWhitespace(keywords));

		static string Unquote(string value)
		{
			if (value.Length < 2)
				return value;

			var quote = value[0];

			if ((quote != '"' && quote != '\'') || value[^1] != quote)
				return value;

			return value[1..^1].Replace($"\\{quote}", quote.ToString());
		}

		static string? CollapseWhitespace(string text)
		{
			var trimmed = WhitespaceRun().Replace(text, " ").Trim();
			return trimmed.Length == 0 ? null : trimmed;
		}
	}

	/// <summary>
	///     Tries to parse a user search query string into a <see cref="UserQuery" />.
	/// </summary>
	/// <param name="s">The raw query text, or <see langword="null" />.</param>
	/// <param name="provider">The format provider, which is ignored.</param>
	/// <param name="result">
	///     The parsed query, or <see cref="Empty" /> when <paramref name="s" /> is
	///     <see langword="null" />.
	/// </param>
	/// <returns>Always <see langword="true" />; this method never reports failure.</returns>
	/// <remarks>
	///     A <see langword="null" /> input produces <see cref="Empty" />; any other input is parsed
	///     via <see cref="Parse" />.
	/// </remarks>
	public static bool TryParse(
		[NotNullWhen(true)] string? s, IFormatProvider? provider,
		[MaybeNullWhen(false)] out UserQuery result)
	{
		if (s is null)
		{
			result = Empty;
			return true;
		}

		result = Parse(s, provider);
		return true;
	}

	/// <summary>
	///     Matches one <c>key(:|=)value</c> token: a bare word key, then either a single- or
	///     double-quoted value (which may contain spaces) or a run of non-whitespace characters.
	/// </summary>
	[GeneratedRegex("""\b(?<key>\w+)(?<op>:|=)(?<value>"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|\S+)""",
		RegexOptions.IgnoreCase)]
	private static partial Regex TokenPattern();

	[GeneratedRegex(@"\s+")]
	private static partial Regex WhitespaceRun();

	/// <summary>Accumulates parsed filters as <see cref="TokenPattern" />'s matches are visited.</summary>
	private sealed class Builder
	{
		private IReadOnlyList<Country>? _countries;
		private ClientPrivileges? _privilege;

		public bool TryApply(string key, string rawValue)
		{
			switch (key)
			{
				case "country":
					if (!TryParseCountries(rawValue, out var countries)) return false;
					_countries = countries;
					return true;
				case "privilege":
					if (Enum.TryParse<ClientPrivileges>(rawValue, true, out var privilege)) return false;
					_privilege = privilege;
					return true;
				default:
					return false;
			}
		}

		/// <summary>
		///     Parses a value naming one or more countries by concatenating their two-letter codes
		///     (e.g. <c>vnusuk</c> for Vietnam, the US, and the UK). Fails if the value's length isn't
		///     a multiple of two, or any two-letter chunk isn't a recognized country -- a partial
		///     result would silently narrow the search rather than fall back to free text.
		/// </summary>
		private static bool TryParseCountries(string rawValue, out IReadOnlyList<Country> countries)
		{
			countries = [];
			if (rawValue.Length == 0 || rawValue.Length % 2 != 0) return false;

			var parsed = new List<Country>(rawValue.Length / 2);
			for (var i = 0; i < rawValue.Length; i += 2)
			{
				if (!Enum.TryParse<Country>(rawValue.AsSpan(i, 2), true, out var country)) return false;
				parsed.Add(country);
			}

			countries = parsed;
			return true;
		}

		public UserQuery Build(string? keywords)
		{
			return new UserQuery(keywords, _countries, _privilege);
		}
	}
}