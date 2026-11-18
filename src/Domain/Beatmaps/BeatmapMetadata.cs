using Basil.Domain.Mechanics;
using Basil.Domain.Shared;

namespace Basil.Domain.Beatmaps;

public sealed class BeatmapMetadata
{
	private const string InvalidOsuFileNameCharacters = ":\\/*<>?\"|";

	public required string Version
	{
		get;
		init => field = string.IsNullOrWhiteSpace(value)
			? throw new ArgumentException("Version cannot be empty.", nameof(value))
			: value.ThrowIfHasNul();
	}

	public required BeatmapDifficulty Difficulty { get; init; }

	public required BeatmapObjectCount ObjectCount { get; init; }

	public string GetFileName(BeatmapsetMetadata beatmapset)
	{
		return NormalizeFileName($"{beatmapset.Name} ({beatmapset.Creator}) [{Version}].osu");
	}

	public static string NormalizeFileName(string fileName)
	{
		ArgumentNullException.ThrowIfNull(fileName);
		return string.Concat(fileName.Where(character => InvalidOsuFileNameCharacters.IndexOf(character) < 0));
	}
}

public abstract class BeatmapObjectCount
{
	public int Total
	{
		get;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}

	public int MaxCombo
	{
		get;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}

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

public sealed class OsuObjectCount : BeatmapObjectCount
{
	public int Circles
	{
		get;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}

	public int Sliders
	{
		get;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}

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

public sealed class TaikoObjectCount : BeatmapObjectCount
{
	public int Hits
	{
		get;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}

	public int DrumRolls
	{
		get;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}

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

public sealed class CatchObjectCount : BeatmapObjectCount
{
	public int Fruits
	{
		get;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}

	public int Droplets
	{
		get;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}

	public int TinyDroplets
	{
		get;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}

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

public sealed class ManiaObjectCount : BeatmapObjectCount
{
	public int Notes
	{
		get;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}

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