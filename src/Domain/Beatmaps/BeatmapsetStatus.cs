namespace Basil.Domain.Beatmaps;

/// <summary>
///     Represents the ranked status of a beatmap.
/// </summary>
public enum BeatmapsetStatus : sbyte
{
	/// <summary>The beatmap has not been submitted.</summary>
	NotSubmitted = -1,

	/// <summary>The beatmap is pending review.</summary>
	Pending = 0,

	/// <summary>A newer version of the beatmap is available.</summary>
	UpdateAvailable = 1,

	/// <summary>The beatmap is ranked.</summary>
	Ranked = 2,

	/// <summary>The beatmap is approved for play.</summary>
	Approved = 3,

	/// <summary>The beatmap is qualified.</summary>
	Qualified = 4,

	/// <summary>The beatmap is loved.</summary>
	Loved = 5
}

/// <summary>
///     Provides conversions between <see cref="BeatmapsetStatus" /> and the numeric status values
///     used by osu! score submission and the osu! web API.
/// </summary>
public static class BeatmapsetStatusExtensions
{
	/// <summary>
	///     Converts a beatmap status to the numeric value returned by the osu! web API.
	/// </summary>
	/// <param name="status">The status to convert.</param>
	/// <returns>The numeric value the osu! web API uses for the status.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="status" /> has no osu! web API mapping, such as
	///     <see cref="BeatmapsetStatus.NotSubmitted" /> or <see cref="BeatmapsetStatus.UpdateAvailable" />.
	/// </exception>
	public static int ToOsuApi(this BeatmapsetStatus status)
	{
		return status switch
		{
			BeatmapsetStatus.Pending => 0,
			BeatmapsetStatus.Ranked => 1,
			BeatmapsetStatus.Approved => 2,
			BeatmapsetStatus.Qualified => 3,
			BeatmapsetStatus.Loved => 4,
			_ => throw new ArgumentOutOfRangeException(nameof(status), status, "No osu!api mapping for this status.")
		};
	}

	/// <summary>
	///     Converts a numeric osu! web API status to a <see cref="BeatmapsetStatus" />.
	/// </summary>
	/// <param name="osuApiStatus">The numeric status value from the osu! web API.</param>
	/// <returns>
	///     The matching status, or <see cref="BeatmapsetStatus.UpdateAvailable" /> when the value is
	///     not recognized.
	/// </returns>
	public static BeatmapsetStatus FromOsuApi(int osuApiStatus)
	{
		return osuApiStatus switch
		{
			-2 => BeatmapsetStatus.Pending, // graveyard
			-1 => BeatmapsetStatus.Pending, // wip
			0 => BeatmapsetStatus.Pending,
			1 => BeatmapsetStatus.Ranked,
			2 => BeatmapsetStatus.Approved,
			3 => BeatmapsetStatus.Qualified,
			4 => BeatmapsetStatus.Loved,
			_ => BeatmapsetStatus.UpdateAvailable
		};
	}

	/// <summary>
	///     Converts a numeric osu!direct status to a <see cref="BeatmapsetStatus" />.
	/// </summary>
	/// <param name="osuDirectStatus">The numeric status value from osu!direct.</param>
	/// <returns>
	///     The matching status, or <see cref="BeatmapsetStatus.UpdateAvailable" /> when the value is
	///     not recognized.
	/// </returns>
	public static BeatmapsetStatus FromOsuDirect(int osuDirectStatus)
	{
		return osuDirectStatus switch
		{
			0 => BeatmapsetStatus.Ranked,
			2 => BeatmapsetStatus.Pending,
			3 => BeatmapsetStatus.Qualified,
			5 => BeatmapsetStatus.Pending, // graveyard
			7 => BeatmapsetStatus.Ranked, // ranked + played before
			8 => BeatmapsetStatus.Loved,
			_ => BeatmapsetStatus.UpdateAvailable
		};
	}
}