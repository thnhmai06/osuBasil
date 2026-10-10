using System.Text.Json.Serialization;
using Basil.Domain.Mechanics;

namespace Basil.Domain.Beatmaps;

/// <summary>
///     Represents the per-mode hit-object counts of a beatmap.
/// </summary>
/// <remarks>
///     <see cref="Total" /> and <see cref="MaxCombo" /> are the only fields every mode shares;
///     everything else is named after that mode's own object types. Each subtype is serialized
///     with a <c>mode</c> discriminator whose values match <see cref="GameMode" />'s own wire
///     values, so a client can dispatch on the same integer either way.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "mode")]
[JsonDerivedType(typeof(OsuObjects), (int)GameMode.Standard)]
[JsonDerivedType(typeof(TaikoObjects), (int)GameMode.Taiko)]
[JsonDerivedType(typeof(CatchObjects), (int)GameMode.Catch)]
[JsonDerivedType(typeof(ManiaObjects), (int)GameMode.Mania)]
public abstract class BeatmapObjects
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

	public static BeatmapObjects NewFrom(GameMode mode)
	{
		return mode switch
		{
			GameMode.Standard => new OsuObjects(),
			GameMode.Taiko => new TaikoObjects(),
			GameMode.Catch => new CatchObjects(),
			GameMode.Mania => new ManiaObjects(),
			_ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown game mode.")
		};
	}
}

/// <summary>
///     Represents the hit-object counts of a standard beatmap.
/// </summary>
public sealed class OsuObjects : BeatmapObjects
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
public sealed class TaikoObjects : BeatmapObjects
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
public sealed class CatchObjects : BeatmapObjects
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
public sealed class ManiaObjects : BeatmapObjects
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