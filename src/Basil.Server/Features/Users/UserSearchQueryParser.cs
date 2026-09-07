using System.Text.RegularExpressions;
using Basil.Server.Features.Users;
using Basil.Domain.Login;

namespace Basil.Server.Features.Users;

/// <summary>
///     Parses <c>GET /users/search</c>'s query syntax (<c>key&lt;operator&gt;value</c> tokens mixed
///     with a free-text id/username portion, e.g. <c>peppy country=jp</c>) into a structured
///     <see cref="UserSearchFilters" />.
/// </summary>
/// <remarks>
///     Only <c>:</c>/<c>=</c> are accepted operators -- unlike
///     <see cref="Basil.Server.Features.Beatmaps.BeatmapsetSearchQueryParser" />, neither
///     supported filter key (<c>country</c>, <c>privilege</c>) has an ordering, so <c>&lt;</c>/<c>&gt;</c>
///     tokens are left as free text rather than given comparison semantics they don't have. A token
///     naming a key this parser doesn't recognize, or a value that fails to parse for the key it
///     named, is likewise left untouched in the free-text portion instead of erroring.
/// </remarks>
public static partial class UserSearchQueryParser
{
	/// <summary>
	///     Matches one <c>key(:|=)value</c> token: a bare word key, then either a single- or
	///     double-quoted value (which may contain spaces) or a run of non-whitespace characters.
	/// </summary>
	[GeneratedRegex("""\b(?<key>\w+)(?<op>:|=)(?<value>"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|\S+)""",
		RegexOptions.IgnoreCase)]
	private static partial Regex TokenPattern();

	/// <summary>Parses a search query string into structured filters plus the remaining free text.</summary>
	/// <param name="query">The raw query text.</param>
	/// <returns>
	///     The parsed <see cref="UserSearchFilters" />, with <see cref="UserSearchFilters.Keywords" />
	///     set to whatever text wasn't consumed by a recognized filter token (or <see langword="null" />
	///     if nothing remains).
	/// </returns>
	public static UserSearchFilters Parse(string? query)
	{
		if (string.IsNullOrWhiteSpace(query)) return UserSearchFilters.Empty;

		var builder = new Builder();
		var keywords = TokenPattern().Replace(query, match =>
		{
			var key = match.Groups["key"].Value.ToLowerInvariant();
			var rawValue = Unquote(match.Groups["value"].Value);
			return builder.TryApply(key, rawValue) ? "" : match.Value;
		});

		return builder.Build(CollapseWhitespace(keywords));
	}

	private static string Unquote(string value)
	{
		if (value.Length < 2) return value;
		var quote = value[0];
		if (quote != '"' && quote != '\'') return value;
		if (value[^1] != quote) return value;
		return value[1..^1].Replace($"\\{quote}", quote.ToString());
	}

	private static string? CollapseWhitespace(string text)
	{
		var trimmed = WhitespaceRun().Replace(text, " ").Trim();
		return trimmed.Length == 0 ? null : trimmed;
	}

	[GeneratedRegex(@"\s+")]
	private static partial Regex WhitespaceRun();

	/// <summary>Accumulates parsed filters as <see cref="TokenPattern" />'s matches are visited.</summary>
	private sealed class Builder
	{
		private IReadOnlyList<Country>? _countries;
		private ushort? _privilegeMask;

		public bool TryApply(string key, string rawValue)
		{
			switch (key)
			{
				case "country":
					if (!TryParseCountries(rawValue, out var countries)) return false;
					_countries = countries;
					return true;
				case "privilege":
					if (!ushort.TryParse(rawValue, out var mask)) return false;
					_privilegeMask = mask;
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

		public UserSearchFilters Build(string? keywords)
		{
			return new UserSearchFilters(keywords, _countries, _privilegeMask);
		}
	}
}