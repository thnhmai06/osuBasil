using System.Text.Json.Serialization;

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
[JsonDerivedType(typeof(OsuBeatmapObjectCounts), (int)GameMode.Standard)]
[JsonDerivedType(typeof(TaikoBeatmapObjectCounts), (int)GameMode.Taiko)]
[JsonDerivedType(typeof(CatchBeatmapObjectCounts), (int)GameMode.Catch)]
[JsonDerivedType(typeof(ManiaBeatmapObjectCounts), (int)GameMode.Mania)]
public abstract record BeatmapObjectCounts
{
	/// <summary>Gets or sets the total number of hit objects in the beatmap.</summary>
	public int Total { get; init; }

	/// <summary>Gets or sets the maximum combo achievable on the beatmap.</summary>
	public int MaxCombo { get; init; }
}

/// <summary>
///     Represents the hit-object counts of a standard beatmap.
/// </summary>
public sealed record OsuBeatmapObjectCounts : BeatmapObjectCounts
{
	/// <summary>Gets or sets the number of circles in the beatmap.</summary>
	public int Circles { get; init; }

	/// <summary>Gets or sets the number of sliders in the beatmap.</summary>
	public int Sliders { get; init; }

	/// <summary>Gets or sets the number of spinners in the beatmap.</summary>
	public int Spinners { get; init; }
}

/// <summary>
///     Represents the hit-object counts of an osu!taiko beatmap.
/// </summary>
public sealed record TaikoBeatmapObjectCounts : BeatmapObjectCounts
{
	/// <summary>Gets or sets the number of hit notes in the beatmap.</summary>
	public int Hits { get; init; }

	/// <summary>Gets or sets the number of drum rolls in the beatmap.</summary>
	public int DrumRolls { get; init; }

	/// <summary>Gets or sets the number of den-den drums in the beatmap.</summary>
	public int Dendens { get; init; }
}

/// <summary>
///     Represents the hit-object counts of an osu!catch beatmap.
/// </summary>
public sealed record CatchBeatmapObjectCounts : BeatmapObjectCounts
{
	/// <summary>Gets or sets the number of fruits in the beatmap.</summary>
	public int Fruits { get; init; }

	/// <summary>Gets or sets the number of droplets in the beatmap.</summary>
	public int Droplets { get; init; }

	/// <summary>Gets or sets the number of tiny droplets in the beatmap.</summary>
	public int TinyDroplets { get; init; }

	/// <summary>Gets or sets the number of bananas in the beatmap.</summary>
	public int Bananas { get; init; }
}

/// <summary>
///     Represents the hit-object counts of an osu!mania beatmap.
/// </summary>
public sealed record ManiaBeatmapObjectCounts : BeatmapObjectCounts
{
	/// <summary>Gets or sets the number of notes in the beatmap.</summary>
	public int Notes { get; init; }

	/// <summary>Gets or sets the number of hold notes in the beatmap.</summary>
	public int HoldNotes { get; init; }
}