using System.Globalization;

namespace Basil.Host.Cli.Commands;

/// <summary>Sizes and durations in the units the console shows and takes: <c>100 MB</c>, <c>30m</c>, <c>12h</c>, <c>7d</c>.</summary>
internal static class Units
{
	private const long Kilo = 1024;
	private const long Mega = Kilo * 1024;
	private const long Giga = Mega * 1024;

	public static string Size(long bytes)
	{
		return bytes switch
		{
			>= Giga => $"{(double)bytes / Giga:0.##} GB",
			>= Mega => $"{(double)bytes / Mega:0.##} MB",
			_ => $"{(double)bytes / Kilo:0.##} KB"
		};
	}

	// A number alone is megabytes, the unit the settings are shown in.
	public static bool TryParseSize(string text, out long bytes)
	{
		bytes = 0;
		var trimmed = text.Trim();
		var scale = Mega;
		foreach (var (suffix, factor) in new[] { ("GB", Giga), ("MB", Mega), ("KB", Kilo), ("B", 1L) })
			if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
			{
				scale = factor;
				trimmed = trimmed[..^suffix.Length].Trim();
				break;
			}

		if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) || amount < 0)
			return false;

		bytes = (long)Math.Round(amount * scale);
		return true;
	}

	public static string Duration(TimeSpan span)
	{
		foreach (var (unit, size) in new[] { ('d', TimeSpan.FromDays(1)), ('h', TimeSpan.FromHours(1)), ('m', TimeSpan.FromMinutes(1)), ('s', TimeSpan.FromSeconds(1)) })
			if (span >= size && span.Ticks % size.Ticks == 0)
				return $"{span.Ticks / size.Ticks}{unit}";

		return $"{span.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)}s";
	}

	public static bool TryParseDuration(string text, out TimeSpan span)
	{
		span = default;
		var trimmed = text.Trim();
		if (trimmed.Length < 2 ||
		    !double.TryParse(trimmed[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) ||
		    amount < 0)
			return false;

		var size = char.ToLowerInvariant(trimmed[^1]) switch
		{
			's' => TimeSpan.FromSeconds(1),
			'm' => TimeSpan.FromMinutes(1),
			'h' => TimeSpan.FromHours(1),
			'd' => TimeSpan.FromDays(1),
			_ => TimeSpan.Zero
		};
		span = size * amount;
		return size != TimeSpan.Zero;
	}
}
