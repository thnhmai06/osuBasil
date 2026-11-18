namespace Basil.Domain.Beatmaps;

public enum BeatmapsetStatus : sbyte
{
	NotSubmitted = -1,

	Pending = 0,

	UpdateAvailable = 1,

	Ranked = 2,

	Approved = 3,

	Qualified = 4,

	Loved = 5
}

public static class BeatmapsetStatusExtensions
{
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