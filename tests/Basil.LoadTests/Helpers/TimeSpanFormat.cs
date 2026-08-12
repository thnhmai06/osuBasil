namespace Basil.LoadTests.Helpers;

/// <summary>Formats durations for human-readable report output.</summary>
public static class TimeSpanFormat
{
	/// <summary>Formats a duration as <c>1h 23m 45s</c>, omitting leading zero components.</summary>
	/// <param name="value">The duration to format.</param>
	/// <returns>The formatted duration.</returns>
	public static string Humanize(TimeSpan value)
	{
		if (value.TotalHours >= 1) return $"{(int)value.TotalHours}h {value.Minutes}m {value.Seconds}s";
		return value.TotalMinutes >= 1 ? $"{value.Minutes}m {value.Seconds}s" : $"{value.TotalSeconds:F1}s";
	}
}