namespace Basil.Application.Formats;

/// <summary>Converts naive, always-UTC persisted timestamps to the offset-aware type the API returns.</summary>
public static class DateTimeExtensions
{
	/// <summary>
	///     Reinterprets a <see cref="DateTime" /> as UTC, regardless of its <see cref="DateTime.Kind" />,
	///     and returns it as a <see cref="DateTimeOffset" /> with a zero offset.
	/// </summary>
	/// <remarks>
	///     Every persisted timestamp in this codebase is stored and read as UTC, but SQLite/Dapper
	///     round-trips <see cref="DateTime" /> values with <see cref="DateTimeKind.Unspecified" />,
	///     which <see cref="DateTimeOffset" />'s implicit conversion would otherwise treat as local
	///     time.
	/// </remarks>
	/// <param name="dateTime">A UTC timestamp, of any <see cref="DateTime.Kind" />.</param>
	/// <returns>The equivalent <see cref="DateTimeOffset" />, with a zero UTC offset.</returns>
	public static DateTimeOffset AsUtcOffset(this DateTime dateTime)
	{
		return new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
	}
}
