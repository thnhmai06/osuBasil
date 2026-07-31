using System.Text.Json.Serialization;

namespace Basil.Domain.Beatmaps;

/// <summary>
///     Per-mode hit-object counts, replacing a mode-agnostic <c>Dictionary&lt;string,int&gt;</c> with one
///     concrete subtype per <see cref="GameMode" /> so the OpenAPI schema can describe the real shape
///     (`oneOf`) instead of an opaque string-keyed map. <see cref="Total" />/<see cref="MaxCombo" /> are
///     the only fields every mode shares; everything else is named after that mode's own object types.
///     The <c>mode</c> discriminator matches <see cref="GameMode" />'s own wire values (plain numbers,
///     per the enum wire convention), so a client can dispatch on the same integer either way.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "mode")]
[JsonDerivedType(typeof(OsuObjectCounts), (int)GameMode.Standard)]
[JsonDerivedType(typeof(TaikoObjectCounts), (int)GameMode.Taiko)]
[JsonDerivedType(typeof(CatchObjectCounts), (int)GameMode.Catch)]
[JsonDerivedType(typeof(ManiaObjectCounts), (int)GameMode.Mania)]
public abstract record ObjectCounts
{
	public int Total { get; init; }
	public int MaxCombo { get; init; }
}

public sealed record OsuObjectCounts : ObjectCounts
{
	public int Circles { get; init; }
	public int Sliders { get; init; }
	public int Spinners { get; init; }
}

public sealed record TaikoObjectCounts : ObjectCounts
{
	public int Hits { get; init; }
	public int DrumRolls { get; init; }
	public int Dendens { get; init; }
}

public sealed record CatchObjectCounts : ObjectCounts
{
	public int Fruits { get; init; }
	public int Droplets { get; init; }
	public int TinyDroplets { get; init; }
	public int Bananas { get; init; }
}

public sealed record ManiaObjectCounts : ObjectCounts
{
	public int Notes { get; init; }
	public int HoldNotes { get; init; }
}
