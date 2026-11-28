namespace Basil.Protocol.Game.Stable.Models.Web;

/// <summary>A beatmapset as an osu!direct list shows it.</summary>
/// <param name="Id">The beatmapset id.</param>
/// <param name="Artist">The artist.</param>
/// <param name="Title">The title.</param>
/// <param name="Creator">The mapper.</param>
/// <param name="RankedStatus">The ranked status number.</param>
/// <param name="UpdatedAt">When the set last changed.</param>
/// <param name="HasVideo">Whether the set has a video.</param>
/// <param name="Difficulties">The difficulties, easiest first, or <see langword="null"/> when the list does not show them.</param>
public sealed record DirectSet(
	int Id,
	string Artist,
	string Title,
	string Creator,
	int RankedStatus,
	DateTimeOffset UpdatedAt,
	bool HasVideo,
	IReadOnlyList<DirectDifficulty>? Difficulties);

/// <summary>One difficulty of a <see cref="DirectSet"/>.</summary>
public sealed record DirectDifficulty(
	double Stars,
	string Name,
	double CircleSize,
	double OverallDifficulty,
	double ApproachRate,
	double HealthDrain,
	int Mode);
