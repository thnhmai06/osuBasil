using Basil.Domain.Mechanics;
using Basil.Domain.Shared;

namespace Basil.Domain.Beatmaps;

/// <summary>
///     What one difficulty of a beatmapset is: its name, gameplay stats and hit objects.
/// </summary>
public sealed class BeatmapMetadata
{
	private const string InvalidOsuFileNameCharacters = ":\\/*<>?\"|";

	/// <summary>The difficulty name, such as "Insane".</summary>
	/// <exception cref="ArgumentException">The name is empty, blank, or contains the NUL character.</exception>
	public required string Version
	{
		get;
		init => field = string.IsNullOrWhiteSpace(value)
			? throw new ArgumentException("Version cannot be empty.", nameof(value))
			: value.ThrowIfHasNul();
	}

	/// <summary>The gameplay stats of the beatmap without mods.</summary>
	public required BeatmapDifficulty Difficulty { get; init; }

	/// <summary>The per-mode hit-object counts of the beatmap.</summary>
	public required BeatmapObjectCount ObjectCount { get; init; }

	/// <summary>Gets the file name osu! uses for this difficulty.</summary>
	/// <param name="beatmapset">The set containing the difficulty.</param>
	/// <returns>The osu! file name, without characters that osu! removes.</returns>
	public string GetFileName(BeatmapsetMetadata beatmapset)
	{
		return NormalizeFileName($"{beatmapset.Name} ({beatmapset.Creator}) [{Version}].osu");
	}

	/// <summary>Removes characters osu! excludes from a beatmap file name.</summary>
	/// <param name="fileName">The file name to normalize.</param>
	/// <returns>The file name without <c>: \ / * &lt; &gt; ? " |</c>.</returns>
	public static string NormalizeFileName(string fileName)
	{
		ArgumentNullException.ThrowIfNull(fileName);
		return string.Concat(fileName.Where(character => InvalidOsuFileNameCharacters.IndexOf(character) < 0));
	}
}

/// <summary>
///     Represents the per-mode hit-object counts of a beatmap.
/// </summary>
/// <remarks>
///     <see cref="Total" /> and <see cref="MaxCombo" /> are the only fields every mode shares;
///     everything else is named after that mode's own object types.
/// </remarks>
public abstract class BeatmapObjectCount
{
	/// <summary>Gets or sets the total number of hit objects in the beatmap.</summary>
	public int Total
	{
		get;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}

	/// <summary>Gets or sets the maximum combo achievable on the beatmap.</summary>
	public int MaxCombo
	{
		get;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}

	/// <summary>Creates the object counts of a beatmap that has no objects.</summary>
	/// <param name="mode">The game mode of the beatmap.</param>
	/// <returns>The empty object counts for that mode.</returns>
	public static BeatmapObjectCount Empty(GameMode mode)
	{
		return mode switch
		{
			GameMode.Standard => new OsuObjectCount(),
			GameMode.Taiko => new TaikoObjectCount(),
			GameMode.Catch => new CatchObjectCount(),
			GameMode.Mania => new ManiaObjectCount(),
			_ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown game mode.")
		};
	}
}

/// <summary>
///     Represents the hit-object counts of a standard beatmap.
/// </summary>
public sealed class OsuObjectCount : BeatmapObjectCount
{
	/// <summary>Gets or sets the number of circles in the beatmap.</summary>
	public int Circles
	{
		get;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}

	/// <summary>Gets or sets the number of sliders in the beatmap.</summary>
	public int Sliders
	{
		get;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}

	/// <summary>Gets or sets the number of spinners in the beatmap.</summary>
	public int Spinners
	{
		get;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}
}

/// <summary>
///     Represents the hit-object counts of an osu!taiko beatmap.
/// </summary>
public sealed class TaikoObjectCount : BeatmapObjectCount
{
	/// <summary>Gets or sets the number of hit notes in the beatmap.</summary>
	public int Hits
	{
		get;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}

	/// <summary>Gets or sets the number of drum rolls in the beatmap.</summary>
	public int DrumRolls
	{
		get;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}

	/// <summary>Gets or sets the number of den-den drums in the beatmap.</summary>
	public int Dendens
	{
		get;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}
}

/// <summary>
///     Represents the hit-object counts of an osu!catch beatmap.
/// </summary>
public sealed class CatchObjectCount : BeatmapObjectCount
{
	/// <summary>Gets or sets the number of fruits in the beatmap.</summary>
	public int Fruits
	{
		get;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}

	/// <summary>Gets or sets the number of droplets in the beatmap.</summary>
	public int Droplets
	{
		get;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}

	/// <summary>Gets or sets the number of tiny droplets in the beatmap.</summary>
	public int TinyDroplets
	{
		get;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}

	/// <summary>Gets or sets the number of bananas in the beatmap.</summary>
	public int Bananas
	{
		get;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}
}

/// <summary>
///     Represents the hit-object counts of an osu!mania beatmap.
/// </summary>
public sealed class ManiaObjectCount : BeatmapObjectCount
{
	/// <summary>Gets or sets the number of notes in the beatmap.</summary>
	public int Notes
	{
		get;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}

	/// <summary>Gets or sets the number of hold notes in the beatmap.</summary>
	public int HoldNotes
	{
		get;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}
}