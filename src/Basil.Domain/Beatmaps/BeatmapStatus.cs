namespace Basil.Domain.Beatmaps;

/// <summary>
///     Represents the ranked status of a beatmap.
/// </summary>
public enum BeatmapStatus : sbyte
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
///     Provides conversions between <see cref="BeatmapStatus" /> and the numeric status values
///     used by osu! score submission and the osu! web API.
/// </summary>
public static class RankedStatusExtensions
{
	/// <summary>
	///     Converts a beatmap status to the numeric value returned by the osu! web API.
	/// </summary>
	/// <param name="status">The status to convert.</param>
	/// <returns>The numeric value the osu! web API uses for the status.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="status" /> has no osu! web API mapping, such as
	///     <see cref="BeatmapStatus.NotSubmitted" /> or <see cref="BeatmapStatus.UpdateAvailable" />.
	/// </exception>
	public static int ToOsuApi(this BeatmapStatus status)
	{
		return status switch
		{
			BeatmapStatus.Pending => 0,
			BeatmapStatus.Ranked => 1,
			BeatmapStatus.Approved => 2,
			BeatmapStatus.Qualified => 3,
			BeatmapStatus.Loved => 4,
			_ => throw new ArgumentOutOfRangeException(nameof(status), status, "No osu!api mapping for this status.")
		};
	}

	/// <summary>
	///     Converts a numeric osu! web API status to a <see cref="BeatmapStatus" />.
	/// </summary>
	/// <param name="osuApiStatus">The numeric status value from the osu! web API.</param>
	/// <returns>
	///     The matching status, or <see cref="BeatmapStatus.UpdateAvailable" /> when the value is
	///     not recognized.
	/// </returns>
	public static BeatmapStatus FromOsuApi(int osuApiStatus)
	{
		return osuApiStatus switch
		{
			-2 => BeatmapStatus.Pending, // graveyard
			-1 => BeatmapStatus.Pending, // wip
			0 => BeatmapStatus.Pending,
			1 => BeatmapStatus.Ranked,
			2 => BeatmapStatus.Approved,
			3 => BeatmapStatus.Qualified,
			4 => BeatmapStatus.Loved,
			_ => BeatmapStatus.UpdateAvailable
		};
	}

	/// <summary>
	///     Converts a numeric osu!direct status to a <see cref="BeatmapStatus" />.
	/// </summary>
	/// <param name="osuDirectStatus">The numeric status value from osu!direct.</param>
	/// <returns>
	///     The matching status, or <see cref="BeatmapStatus.UpdateAvailable" /> when the value is
	///     not recognized.
	/// </returns>
	public static BeatmapStatus FromOsuDirect(int osuDirectStatus)
	{
		return osuDirectStatus switch
		{
			0 => BeatmapStatus.Ranked,
			2 => BeatmapStatus.Pending,
			3 => BeatmapStatus.Qualified,
			5 => BeatmapStatus.Pending, // graveyard
			7 => BeatmapStatus.Ranked, // ranked + played before
			8 => BeatmapStatus.Loved,
			_ => BeatmapStatus.UpdateAvailable
		};
	}
}
