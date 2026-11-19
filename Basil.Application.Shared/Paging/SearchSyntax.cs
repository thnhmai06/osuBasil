using System.Globalization;
using System.Text.RegularExpressions;

namespace Basil.Application.Shared.Paging;

public static partial class SearchSyntax
{
	public static string? Parse(string text, Func<string, string, string, bool> apply)
	{
		var result = TokenPattern().Replace(text, match =>
		{
			var key = match.Groups["key"].Value.ToLowerInvariant();
			var op = match.Groups["op"].Value;
			var rawValue = Unquote(match.Groups["value"].Value);
			return apply(key, op, rawValue) ? "" : match.Value;
		});

		var collapsed = WhitespaceRun().Replace(result, " ").Trim();
		return collapsed.Length == 0 ? null : collapsed;

		static string Unquote(string value)
		{
			if (value.Length < 2) return value;
			var quote = value[0];
			if ((quote != '"' && quote != '\'') || value[^1] != quote) return value;
			return value[1..^1].Replace($"\\{quote}", quote.ToString());
		}
	}

	public static Interval<T> Narrow<T>(Interval<T>? current, string op, T value)
		where T : struct, IComparable<T>
	{
		var min = current?.Min;
		var max = current?.Max;
		var minInclusive = current?.MinInclusive ?? true;
		var maxInclusive = current?.MaxInclusive ?? true;

		switch (op)
		{
			case ":":
			case "=":
				min = value;
				max = value;
				minInclusive = true;
				maxInclusive = true;
				break;
			case "<":
				max = value;
				maxInclusive = false;
				break;
			case "<=":
				max = value;
				maxInclusive = true;
				break;
			case ">":
				min = value;
				minInclusive = false;
				break;
			case ">=":
				min = value;
				minInclusive = true;
				break;
		}

		return new Interval<T>(min, max, minInclusive, maxInclusive);
	}

	public static Interval<DateTimeOffset>? NarrowDate(Interval<DateTimeOffset>? current, string op, string raw)
	{
		if (!TryParseDateWindow(raw, out var start, out var end))
			return null;

		var min = current?.Min;
		var max = current?.Max;
		var minInclusive = current?.MinInclusive ?? true;
		var maxInclusive = current?.MaxInclusive ?? true;

		var isZeroLength = start == end;

		switch (op)
		{
			case ":":
			case "=":
				min = start;
				max = isZeroLength ? start : end;
				minInclusive = true;
				maxInclusive = isZeroLength;
				break;
			case "<":
				max = start;
				maxInclusive = false;
				break;
			case "<=":
				max = end;
				maxInclusive = isZeroLength;
				break;
			case ">":
				min = isZeroLength ? start : end;
				minInclusive = !isZeroLength;
				break;
			case ">=":
				min = start;
				minInclusive = true;
				break;
		}

		return new Interval<DateTimeOffset>(min, max, minInclusive, maxInclusive);
	}

	private static bool TryParseDateWindow(string raw, out DateTimeOffset start, out DateTimeOffset end)
	{
		const DateTimeStyles utc = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;

		if (DateTimeOffset.TryParseExact(raw, "yyyy", CultureInfo.InvariantCulture, utc, out start))
		{
			end = start.AddYears(1);
			return true;
		}

		if (DateTimeOffset.TryParseExact(raw, "yyyy-MM", CultureInfo.InvariantCulture, utc, out start))
		{
			end = start.AddMonths(1);
			return true;
		}

		if (DateTimeOffset.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, utc, out start))
		{
			end = start.AddDays(1);
			return true;
		}

		if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var exact))
		{
			start = exact;
			end = exact;
			return true;
		}

		start = default;
		end = default;
		return false;
	}

	public static bool TryParseState(string raw, out bool? value)
	{
		value = raw switch { "1" => true, "0" => false, _ => null };
		return raw is "1" or "0" or "*";
	}

	public static bool TryParseSeconds(string raw, out double seconds)
	{
		seconds = 0;
		var match = LengthPattern().Match(raw);
		if (!match.Success) return false;
		if (!double.TryParse(match.Groups["num"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
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

	[GeneratedRegex("""\b(?<key>\w+)(?<op>:|=|[<>]=?)(?<value>"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|\S+)""",
		RegexOptions.IgnoreCase)]
	private static partial Regex TokenPattern();

	[GeneratedRegex(@"\s+")]
	private static partial Regex WhitespaceRun();

	[GeneratedRegex(@"^(?<num>[\d.]+)(?<unit>ms|s|m|h)?$", RegexOptions.IgnoreCase)]
	private static partial Regex LengthPattern();
}