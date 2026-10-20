using Basil.Domain.Common;

namespace Basil.Domain.Beatmaps;

/// <summary>
///     What one difficulty of a beatmapset is: its name, gameplay stats and hit objects.
/// </summary>
public sealed class BeatmapMetadata
{
	private const string InvalidOsuFileNameCharacters = ":\\/*<>?\"|";

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

	/// <summary>Gets the file name osu! uses for this difficulty.</summary>
	/// <param name="beatmapset">The set containing the difficulty.</param>
	/// <returns>The osu! file name, without characters that osu! removes.</returns>
	public string OsuFileName(BeatmapsetMetadata beatmapset)
	{
		return NormalizeOsuFileName(
			$"{beatmapset.Artist} - {beatmapset.Title} ({beatmapset.Creator}) [{Version}].osu");
	}

	/// <summary>Removes characters osu! excludes from a beatmap file name.</summary>
	/// <param name="fileName">The file name to normalize.</param>
	/// <returns>The file name without <c>: \ / * &lt; &gt; ? " |</c>.</returns>
	public static string NormalizeOsuFileName(string fileName)
	{
		ArgumentNullException.ThrowIfNull(fileName);
		return string.Concat(fileName.Where(character => InvalidOsuFileNameCharacters.IndexOf(character) < 0));
	}
}