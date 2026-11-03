namespace Basil.Protocol.OsuStable.Models.Wire;

/// <summary>The beatmap ranked status numbers the osu! client expects in leaderboard and beatmap info lines.</summary>
public enum WireBeatmapStatus : sbyte
{
	/// <summary>The beatmap is not submitted.</summary>
	NotSubmitted = -1,

	/// <summary>The beatmap is pending.</summary>
	Pending = 0,

	/// <summary>The beatmap has an update available.</summary>
	UpdateAvailable = 1,

	/// <summary>The beatmap is ranked.</summary>
	Ranked = 2,

	/// <summary>The beatmap is approved.</summary>
	Approved = 3,

	/// <summary>The beatmap is qualified.</summary>
	Qualified = 4,

	/// <summary>The beatmap is loved.</summary>
	Loved = 5
}
