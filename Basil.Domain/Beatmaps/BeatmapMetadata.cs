using Basil.Domain.Game;
using Basil.Shared.Utilities;

namespace Basil.Domain.Beatmaps;

/// <summary>What identifies and describes one difficulty of a beatmapset.</summary>
public sealed class BeatmapMetadata
{
	/// <summary>The name of the difficulty, such as "Insane"; neither empty nor containing a NUL character.</summary>
	public required string VersionName
	{
		get;
		init => field = string.IsNullOrWhiteSpace(value)
			? throw new ArgumentException("Name cannot be empty.", nameof(value))
			: value.ThrowIfHasNul();
	}

	/// <summary>The difficulty values of the beatmap.</summary>
	public required BeatmapDifficulty Difficulty { get; init; }

	/// <summary>How many hit objects the beatmap has, of the kinds its mode uses.</summary>
	public required BeatmapObjectCount ObjectCount { get; init; }
}

/// <summary>The number of hit objects in a beatmap, shared by every mode.</summary>
public abstract class BeatmapObjectCount
{
	/// <summary>The number of hit objects.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
	public int Total
	{
		get;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}

	/// <summary>The highest combo a play can reach.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
	public int MaxCombo
	{
		get;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}

	/// <summary>Creates a count with every number at zero, of the kind the mode uses.</summary>
	/// <param name="mode">The game mode of the beatmap.</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a defined mode.</exception>
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

/// <summary>The hit objects of a standard-mode beatmap.</summary>
public sealed class OsuObjectCount : BeatmapObjectCount
{
	/// <summary>The number of hit circles.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
	public int Circles
	{
		get;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}

	/// <summary>The number of sliders.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
	public int Sliders
	{
		get;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}

	/// <summary>The number of spinners.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
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

/// <summary>The hit objects of a taiko beatmap.</summary>
public sealed class TaikoObjectCount : BeatmapObjectCount
{
	/// <summary>The number of single drum hits.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
	public int Hits
	{
		get;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}

	/// <summary>The number of drum rolls.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
	public int DrumRolls
	{
		get;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}

	/// <summary>The number of dendens, the spinners of taiko.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
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

/// <summary>The hit objects of a catch beatmap.</summary>
public sealed class CatchObjectCount : BeatmapObjectCount
{
	/// <summary>The number of fruits.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
	public int Fruits
	{
		get;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}

	/// <summary>The number of droplets.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
	public int Droplets
	{
		get;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}

	/// <summary>The number of tiny droplets.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
	public int TinyDroplets
	{
		get;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}

	/// <summary>The number of bananas.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
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

/// <summary>The hit objects of a mania beatmap.</summary>
public sealed class ManiaObjectCount : BeatmapObjectCount
{
	/// <summary>The number of single notes.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
	public int Notes
	{
		get;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}

	/// <summary>The number of hold notes.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
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