using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using Basil.Domain.Beatmaps;

namespace Basil.Application.Models.Queries;

/// <summary>
///     Parses the beatmapset-level portion of osu!'s beatmap search query syntax
///     (<c>key&lt;operator&gt;value</c> tokens mixed with free-text keywords, e.g.
///     <c>camellia status=ranked</c>) into a structured <see cref="BeatmapsetQuery" />.
/// </summary>
/// <remarks>
///     Backs both <c>GET /web/osu-search.php</c> (the in-game osu!direct panel) and
///     <c>GET /beatmapsets/search</c> (the REST equivalent), so the same query text behaves
///     identically on both. A token naming a key this parser doesn't recognize -- the beatmap-level
///     keys (stars, ar, cs, od, hp, bpm, length, keys, circles, sliders), which are parsed by
///     <see cref="BeatmapQuery" /> instead, a genuine typo, or one of osu!'s keys Basil has no data
///     for (see <see cref="BeatmapsetQuery" />'s own remarks) -- is left untouched in the free-text
///     portion rather than rejected, matching osu!web's own graceful degradation.
/// </remarks>
public sealed partial record BeatmapsetQuery
{
	/// <summary>
	///     Parses the beatmapset-level portion of osu!'s beatmap search query syntax into a
	///     <see cref="BeatmapsetQuery" />.
	/// </summary>
	/// <param name="query">The search query text to parse.</param>
	/// <param name="provider">Ignored; numeric parsing always uses the invariant culture.</param>
	/// <returns>
	///     The parsed query. Tokens that name an unknown key or carry an unparsable value are left
	///     in the free-text keywords instead of being rejected.
	/// </returns>
	public static BeatmapsetQuery Parse(string query, IFormatProvider? provider)
	{
		var builder = new Builder();
		var keywords = TokenPattern().Replace(query, match =>
		{
			var key = match.Groups["key"].Value.ToLowerInvariant();
			var opText = match.Groups["op"].Value;
			var op = ComparisonOperatorExtensions.Parse(opText);
			var rawValue = Unquote(match.Groups["value"].Value);

			// A key this switch doesn't handle, or a value that fails to parse for the key it named,
			// is left exactly as written -- it becomes part of the free-text keywords instead of
			// being silently dropped or rejecting the whole query.
			return builder.TryApply(key, op, rawValue) ? "" : match.Value;
		});

		return builder.Build(CollapseWhitespace(keywords));

		static string Unquote(string value)
		{
			if (value.Length < 2) return value;
			var quote = value[0];
			if ((quote != '"' && quote != '\'') || value[^1] != quote) return value;
			return value[1..^1].Replace($"\\{quote}", quote.ToString());
		}

		static string? CollapseWhitespace(string text)
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
	///     yields <see cref="BeatmapsetQuery.Empty" />.
	/// </returns>
	public static bool TryParse(
		[NotNullWhen(true)] string? s,
		IFormatProvider? provider,
		[MaybeNullWhen(false)] out BeatmapsetQuery result)
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
		private string? _artist;
		private DateQuery? _created;
		private string? _creator;
		private string? _difficulty;
		private BeatmapStatus? _status;
		private string? _title;
		private DateQuery? _updated;

		public bool TryApply(string key, ComparisonOperator op, string rawValue)
		{
			switch (key)
			{
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

		public BeatmapsetQuery Build(string? keywords)
		{
			return new BeatmapsetQuery(keywords, Creator: _creator, Artist: _artist, Title: _title,
				Difficulty: _difficulty, Status: _status, Created: _created, Updated: _updated);
		}

		private bool TrySetStatus(string raw)
		{
			// osu!'s own syntax accepts a prefix of the status name; Basil only ever reports one
			// status for every beatmapset (see BeatmapsetQuery.Status's own remarks), so this
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
				_ => null
			};
			return _status is not null;
		}

		private static bool TrySetDate(string raw, ComparisonOperator op, Action<DateQuery> set)
		{
			if (!TryParseDateWindow(raw, out var start, out var end)) return false;
			set(new DateQuery(op, start, end));
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