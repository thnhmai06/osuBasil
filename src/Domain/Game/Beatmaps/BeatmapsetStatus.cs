namespace Basil.Domain.Game.Beatmaps;

public enum BeatmapsetStatus : sbyte
{
	// NotSubmitted = -1, // not used

	Pending = 0, // graveyard too

	// UpdateAvailable = 1, // not used

	Ranked = 2,

	Approved = 3,

	Qualified = 4,

	Loved = 5
}

public static class BeatmapsetStatusExtensions
{
	public static BeatmapsetStatus FromOsuDirect(int osuDirectStatus) // TODO: Move to Protocol
	{
		return osuDirectStatus switch
		{
			0 => BeatmapsetStatus.Ranked,
			2 => BeatmapsetStatus.Pending,
			3 => BeatmapsetStatus.Qualified,
			5 => BeatmapsetStatus.Pending, // graveyard
			7 => BeatmapsetStatus.Ranked, // ranked + played before
			8 => BeatmapsetStatus.Loved,
			_ => BeatmapsetStatus.Pending
			// _ => BeatmapsetStatus.UpdateAvailable
		};
	}

	// not used

	// public static int ToOsuApi(this BeatmapsetStatus status)
	// {
	// 	return status switch
	// 	{
	// 		BeatmapsetStatus.Pending => 0,
	// 		BeatmapsetStatus.Ranked => 1,
	// 		BeatmapsetStatus.Approved => 2,
	// 		BeatmapsetStatus.Qualified => 3,
	// 		BeatmapsetStatus.Loved => 4,
	// 		_ => throw new ArgumentOutOfRangeException(nameof(status), status, "No osu!api mapping for this status.")
	// 	};
	// }
	//
	// public static BeatmapsetStatus FromOsuApi(int osuApiStatus)
	// {
	// 	return osuApiStatus switch
	// 	{
	// 		-2 => BeatmapsetStatus.Pending, // graveyard
	// 		-1 => BeatmapsetStatus.Pending, // wip
	// 		0 => BeatmapsetStatus.Pending,
	// 		1 => BeatmapsetStatus.Ranked,
	// 		2 => BeatmapsetStatus.Approved,
	// 		3 => BeatmapsetStatus.Qualified,
	// 		4 => BeatmapsetStatus.Loved,
	// 		_ => BeatmapsetStatus.Pending
	// 		// _ => BeatmapsetStatus.UpdateAvailable
	// 	};
	// }
}