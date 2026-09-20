using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Basil.Application.Queries;

/// <summary>
///     Parses the beatmap-level portion of osu!'s beatmap search query syntax
///     (<c>key&lt;operator&gt;value</c> tokens mixed with free-text keywords, e.g.
///     <c>stars&gt;5 ar=9 keys=7</c>) into a structured <see cref="BeatmapQuery" />.
/// </summary>
/// <remarks>
///     A token naming a key this parser doesn't recognize -- either a set-level key handled by
///     <see cref="BeatmapsetQuery" /> (artist, title, creator, status, created, updated, ...) or one
///     of osu!'s keys Basil has no data for -- is left untouched in the free-text portion rather
///     than rejected, matching osu!web's own graceful degradation.
/// </remarks>
public sealed partial record BeatmapQuery
{
	/// <summary>
	///     Parses osu!'s beatmap search query syntax into a <see cref="BeatmapQuery" />.
	/// </summary>
	/// <param name="query">The search query text to parse.</param>
	/// <param name="provider">Ignored; numeric parsing always uses the invariant culture.</param>
	/// <returns>
	///     The parsed query. Tokens that name an unknown key or carry an unparsable value are left
	///     in the free-text keywords instead of being rejected.
	/// </returns>
	public static BeatmapQuery Parse(string query, IFormatProvider? provider)
	{
		var builder = new Builder();
		var keywords = TokenPattern().Replace(query, match =>
		{
			var key = match.Groups["key"].Value.ToLowerInvariant();
			var opText = match.Groups["op"].Value;
			var op = ComparisonOperatorExtensions.Parse(opText);
			var rawValue = Unquote(match.Groups["value"].Value);

			return builder.TryApply(key, op, rawValue) ? "" : match.Value;
		});

		return builder.Build(keywords);

		static string Unquote(string value)
		{
			if (value.Length < 2) return value;
			var quote = value[0];
			if ((quote != '"' && quote != '\'') || value[^1] != quote) return value;
			return value[1..^1].Replace($"\\{quote}", quote.ToString());
		}

		static string? CollapseWhitespace(string text) // why not use that?
		{
			var trimmed = WhitespaceRun().Replace(text, " ").Trim();
			return trimmed.Length == 0 ? null : trimmed;
		}
	}

	/// <summary>
	///     Attempts to parse a search query.
	/// </summary>
	/// <param name="s">The query text, or <see langword="null" /> to represent an empty query.</param>
	/// <param name="provider">Ignored; numeric parsing always uses the invariant culture.</param>
	/// <param name="result">The parsed query.</param>
	/// <returns>
	///     Always <see langword="true" />: parsing never fails, and a <see langword="null" /> input
	///     yields <see cref="BeatmapQuery.Empty" />.
	/// </returns>
	public static bool TryParse(
		[NotNullWhen(true)] string? s,
		IFormatProvider? provider,
		[MaybeNullWhen(false)] out BeatmapQuery result)
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
	///     Matches one <c>key&lt;operator&gt;value</c> token: a bare word key, a <c>:</c>/<c>=</c>/
	///     <c>&lt;</c>/<c>&lt;=</c>/<c>&gt;</c>/<c>&gt;=</c> operator, then either a single- or
	///     double-quoted value (which may contain spaces) or a run of non-whitespace characters.
	/// </summary>
	[GeneratedRegex("""\b(?<key>\w+)(?<op>:|=|[<>]=?)(?<value>"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|\S+)""",
		RegexOptions.IgnoreCase)]
	private static partial Regex TokenPattern();

	[GeneratedRegex(@"\s+")]
	private static partial Regex WhitespaceRun();

	/// <summary>Accumulates parsed filters as <see cref="TokenPattern" />'s matches are visited.</summary>
	private sealed partial class Builder
	{
		private ComparableFilter<double>? _ar;
		private ComparableFilter<double>? _bpm;
		private ComparableFilter<int>? _circles;
		private ComparableFilter<double>? _cs;
		private ComparableFilter<double>? _hp;
		private ComparableFilter<double>? _keys;
		private ComparableFilter<double>? _length;
		private ComparableFilter<double>? _od;
		private ComparableFilter<int>? _sliders;
		private ComparableFilter<double>? _star;

		public bool TryApply(string key, ComparisonOperator op, string rawValue)
		{
			return key switch
			{
				"stars" or "star" => TrySetDouble(rawValue, op, v => _star = v),
				"ar" => TrySetDouble(rawValue, op, v => _ar = v),
				"dr" or "hp" => TrySetDouble(rawValue, op, v => _hp = v),
				"cs" => TrySetDouble(rawValue, op, v => _cs = v),
				"od" => TrySetDouble(rawValue, op, v => _od = v),
				"bpm" => TrySetDouble(rawValue, op, v => _bpm = v),
				"keys" or "key" => TrySetDouble(rawValue, op, v => _keys = v),
				"circles" => TrySetInt(rawValue, op, v => _circles = v),
				"sliders" => TrySetInt(rawValue, op, v => _sliders = v),
				"length" => TrySetLength(rawValue, op),
				_ => false
			};
		}

		public BeatmapQuery Build(string? keyword)
		{
			return new BeatmapQuery(Keywords: keyword,
				Bpm: _bpm, Length: _length, Cs: _cs, Ar: _ar, Od: _od, Hp: _hp, Star: _star, Keys: _keys,
				Circles: _circles, Sliders: _sliders);
		}

		private static bool TrySetDouble(string raw, ComparisonOperator op, Action<ComparableFilter<double>> set)
		{
			if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return false;
			set(new ComparableFilter<double>(op, value));
			return true;
		}

		private static bool TrySetInt(string raw, ComparisonOperator op, Action<ComparableFilter<int>> set)
		{
			if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) return false;
			set(new ComparableFilter<int>(op, value));
			return true;
		}

		private bool TrySetLength(string raw, ComparisonOperator op)
		{
			if (!TryParseLengthSeconds(raw, out var seconds)) return false;
			_length = new ComparableFilter<double>(op, seconds);
			return true;
		}

		private static bool TryParseLengthSeconds(string raw, out double seconds)
		{
			seconds = 0;
			var match = LengthPattern().Match(raw);
			if (!match.Success) return false;
			if (!double.TryParse(match.Groups["num"].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
				    out var num))
				return false;

			seconds = match.Groups["unit"].Value.ToLowerInvariant() switch
			{
				"ms" => num / 1000,
				"m" => num * 60,
				"h" => num * 3600,
				_ => num // bare number or explicit "s" both mean seconds
			};
			return true;
		}

		[GeneratedRegex(@"^(?<num>[\d.]+)(?<unit>ms|s|m|h)?$", RegexOptions.IgnoreCase)]
		private static partial Regex LengthPattern();
	}
}