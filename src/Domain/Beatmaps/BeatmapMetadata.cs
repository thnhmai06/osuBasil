using Basil.Domain.Common;

namespace Basil.Domain.Beatmaps;

/// <summary>
///     What one difficulty of a beatmapset is: its name, gameplay stats and hit objects.
/// </summary>
public sealed class BeatmapMetadata
{
	/// <summary>The difficulty name, such as "Insane".</summary>
	public required string Version
	{
		get;
		init => field = value.ThrowIfHasNul();
	}

	/// <summary>The gameplay stats of the beatmap without mods.</summary>
	public required BeatmapDifficulty Difficulty { get; init; }

	/// <summary>The per-mode hit-object counts of the beatmap.</summary>
	public required BeatmapObjects Objects { get; init; }
}