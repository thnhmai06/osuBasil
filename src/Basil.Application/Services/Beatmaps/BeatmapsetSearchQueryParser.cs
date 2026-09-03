using System.Globalization;
using System.Text.RegularExpressions;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Domain.Beatmaps;

namespace Basil.Application.Services.Beatmaps;

/// <summary>
///     Parses osu!'s beatmap search query syntax (<c>key&lt;operator&gt;value</c> tokens mixed with
///     free-text keywords, e.g. <c>camellia stars&gt;5 ar=9</c>) into a structured
///     <see cref="BeatmapsetSearchFilters" />.
/// </summary>
/// <remarks>
///     Backs both <c>GET /web/osu-search.php</c> (the in-game osu!direct panel) and
///     <c>GET /beatmapsets/search</c> (the REST equivalent), so the same query text behaves
///     identically on both. A token naming a key this parser doesn't recognize -- either a genuine
///     typo or one of osu!'s keys Basil has no data for (see <see cref="BeatmapsetSearchFilters" />'s
///     own remarks) -- is left untouched in the free-text portion rather than rejected, matching
///     osu!web's own graceful degradation.
/// </remarks>
public static partial class BeatmapsetSearchQueryParser
{
	/// <summary>
	///     Matches one <c>key&lt;operator&gt;value</c> token: a bare word key, a <c>:</c>/<c>=</c>/
	///     <c>&lt;</c>/<c>&lt;=</c>/<c>&gt;</c>/<c>&gt;=</c> operator, then either a single- or
	///     double-quoted value (which may contain spaces) or a run of non-whitespace characters.
	/// </summary>
	[GeneratedRegex("""\b(?<key>\w+)(?<op>:|=|[<>]=?)(?<value>"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|\S+)""",
		RegexOptions.IgnoreCase)]
	private static partial Regex TokenPattern();

	/// <summary>Parses a search query string into structured filters plus the remaining free text.</summary>
	/// <param name="query">The raw query text.</param>
	/// <returns>
	///     The parsed <see cref="BeatmapsetSearchFilters" />, with <see cref="BeatmapsetSearchFilters.Keywords" />
	///     set to whatever text wasn't consumed by a recognized filter token (or <see langword="null" />
	///     if nothing remains).
	/// </returns>
	public static BeatmapsetSearchFilters Parse(string? query)
	{
		if (string.IsNullOrWhiteSpace(query)) return BeatmapsetSearchFilters.Empty;

		var builder = new Builder();
		var keywords = TokenPattern().Replace(query, match =>
		{
			var key = match.Groups["key"].Value.ToLowerInvariant();
			var opText = match.Groups["op"].Value;
			var op = opText is ":" or "=" ? ComparisonOperator.Equal : ParseOperator(opText);
			var rawValue = Unquote(match.Groups["value"].Value);

			// A key this switch doesn't handle, or a value that fails to parse for the key it named,
			// is left exactly as written -- it becomes part of the free-text keywords instead of
			// being silently dropped or rejecting the whole query.
			return builder.TryApply(key, op, rawValue) ? "" : match.Value;
		});

		return builder.Build(CollapseWhitespace(keywords));
	}

	private static ComparisonOperator ParseOperator(string op)
	{
		return op switch
		{
			"<" => ComparisonOperator.LessThan,
			"<=" => ComparisonOperator.LessThanOrEqual,
			">" => ComparisonOperator.GreaterThan,
			">=" => ComparisonOperator.GreaterThanOrEqual,
			_ => ComparisonOperator.Equal
		};
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
	private sealed partial class Builder
	{
		private ComparableFilter<double>? _ar;
		private ComparableFilter<double>? _bpm;
		private ComparableFilter<int>? _circles;
		private DateFilter? _created;
		private string? _creator;
		private ComparableFilter<double>? _cs;
		private string? _difficulty;
		private ComparableFilter<double>? _hp;
		private ComparableFilter<double>? _keys;
		private ComparableFilter<double>? _lengthSeconds;
		private ComparableFilter<double>? _od;
		private ComparableFilter<int>? _sliders;
		private ComparableFilter<double>? _stars;
		private BeatmapStatus? _status;
		private DateFilter? _updated;
		private string? _artist;
		private string? _title;

		public bool TryApply(string key, ComparisonOperator op, string rawValue)
		{
			switch (key)
			{
				case "stars" or "star": return TrySetDouble(rawValue, op, v => _stars = v);
				case "ar": return TrySetDouble(rawValue, op, v => _ar = v);
				case "dr" or "hp": return TrySetDouble(rawValue, op, v => _hp = v);
				case "cs": return TrySetDouble(rawValue, op, v => _cs = v);
				case "od": return TrySetDouble(rawValue, op, v => _od = v);
				case "bpm": return TrySetDouble(rawValue, op, v => _bpm = v);
				case "keys" or "key": return TrySetDouble(rawValue, op, v => _keys = v);
				case "circles": return TrySetInt(rawValue, op, v => _circles = v);
				case "sliders": return TrySetInt(rawValue, op, v => _sliders = v);
				case "length": return TrySetLength(rawValue, op);
				case "creator":
					_creator = rawValue;
					return true;
				case "artist":
					_artist = rawValue;
					return true;
				case "title":
					_title = rawValue;
					return true;
				case "difficulty":
					_difficulty = rawValue;
					return true;
				case "status": return TrySetStatus(rawValue);
				case "created" or "submitted": return TrySetDate(rawValue, op, v => _created = v);
				case "updated": return TrySetDate(rawValue, op, v => _updated = v);
				default: return false;
			}
		}

		public BeatmapsetSearchFilters Build(string? keywords)
		{
			return new BeatmapsetSearchFilters(keywords, _stars, _ar, _hp, _cs, _od, _bpm, _lengthSeconds, _keys,
				_circles, _sliders, _creator, _artist, _title, _difficulty, _status, _created, _updated);
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
			_lengthSeconds = new ComparableFilter<double>(op, seconds);
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

		private bool TrySetStatus(string raw)
		{
			// osu!'s own syntax accepts a prefix of the status name; Basil only ever reports one
			// status for every beatmapset (see BeatmapsetSearchFilters.Status's own remarks), so this
			// resolves the name to compare against rather than trying to search by it.
			var name = raw.ToLowerInvariant();
			_status = name switch
			{
				_ when "pending".StartsWith(name, StringComparison.Ordinal) => BeatmapStatus.Pending,
				_ when "graveyard".StartsWith(name, StringComparison.Ordinal) => BeatmapStatus.Pending,
				_ when "wip".StartsWith(name, StringComparison.Ordinal) => BeatmapStatus.Pending,
				_ when "ranked".StartsWith(name, StringComparison.Ordinal) => BeatmapStatus.Ranked,
				_ when "approved".StartsWith(name, StringComparison.Ordinal) => BeatmapStatus.Approved,
				_ when "qualified".StartsWith(name, StringComparison.Ordinal) => BeatmapStatus.Qualified,
				_ when "loved".StartsWith(name, StringComparison.Ordinal) => BeatmapStatus.Loved,
				_ => (BeatmapStatus?)null
			};
			return _status is not null;
		}

		private static bool TrySetDate(string raw, ComparisonOperator op, Action<DateFilter> set)
		{
			if (!TryParseDateWindow(raw, out var start, out var end)) return false;
			set(new DateFilter(op, start, end));
			return true;
		}

		private static bool TryParseDateWindow(string raw, out DateTimeOffset start, out DateTimeOffset end)
		{
			if (YearOnlyPattern().IsMatch(raw) && int.TryParse(raw, out var year))
			{
				start = new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.Zero);
				end = start.AddYears(1);
				return true;
			}

			if (YearMonthPattern().Match(raw) is { Success: true } ym)
			{
				start = new DateTimeOffset(int.Parse(ym.Groups[1].Value), int.Parse(ym.Groups[2].Value), 1, 0, 0, 0,
					TimeSpan.Zero);
				end = start.AddMonths(1);
				return true;
			}

			if (YearMonthDayPattern().Match(raw) is { Success: true } ymd)
			{
				start = new DateTimeOffset(int.Parse(ymd.Groups[1].Value), int.Parse(ymd.Groups[2].Value),
					int.Parse(ymd.Groups[3].Value), 0, 0, 0, TimeSpan.Zero);
				end = start.AddDays(1);
				return true;
			}

			if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal,
				    out var exact))
			{
				start = exact;
				end = exact;
				return true;
			}

			start = default;
			end = default;
			return false;
		}

		[GeneratedRegex(@"^\d{4}$")]
		private static partial Regex YearOnlyPattern();

		[GeneratedRegex(@"^(\d{4})-(\d{2})$")]
		private static partial Regex YearMonthPattern();

		[GeneratedRegex(@"^(\d{4})-(\d{2})-(\d{2})$")]
		private static partial Regex YearMonthDayPattern();
	}
}