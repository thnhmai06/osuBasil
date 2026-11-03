using System.Globalization;

namespace Basil.Host.Logging;

/// <summary>Reads sizes written in the settings, such as <c>100MB</c>.</summary>
public static class ByteSize
{
	/// <summary>
	///     Reads a size in bytes from a number with an optional <c>B</c>, <c>KB</c>, <c>MB</c> or <c>GB</c> unit (powers
	///     of 1024).
	/// </summary>
	/// <param name="text">The size; empty means no limit.</param>
	/// <param name="setting">The name of the setting, for the message of an invalid value.</param>
	/// <returns>The size in bytes; 0 for no limit.</returns>
	/// <exception cref="FormatException">The text is not a size.</exception>
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