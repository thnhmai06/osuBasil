using System.Globalization;

namespace Basil.Logging;

public static class ByteSize
{
	public static long Parse(string? text, string setting)
	{
		if (string.IsNullOrWhiteSpace(text)) return 0;

		var trimmed = text.Trim();
		var digits = trimmed.AsSpan().TrimEnd("BKMGbkmg ".AsSpan()).Length;
		var unit = trimmed[digits..].Trim().ToUpperInvariant();
		var factor = unit switch
		{
			"" or "B" => 1,
			"KB" => 1L << 10,
			"MB" => 1L << 20,
			"GB" => 1L << 30,
			_ => 0
		};
		if (factor == 0 || !long.TryParse(trimmed[..digits], NumberStyles.None, CultureInfo.InvariantCulture,
			    out var number))
			throw new FormatException($"{setting} is \"{text}\", which is not a size such as 100MB.");

		return number * factor;
	}
}