using Basil.Domain.Beatmaps;

namespace Basil.Application.Abstractions.Beatmaps;

/// <summary>The comparison an individual <see cref="ComparableFilter{T}" /> applies.</summary>
public enum ComparisonOperator
{
	/// <summary>The stored value must equal the filter's value.</summary>
	Equal,

	/// <summary>The stored value must be less than the filter's value.</summary>
	LessThan,

	/// <summary>The stored value must be less than or equal to the filter's value.</summary>
	LessThanOrEqual,

	/// <summary>The stored value must be greater than the filter's value.</summary>
	GreaterThan,

	/// <summary>The stored value must be greater than or equal to the filter's value.</summary>
	GreaterThanOrEqual
}

/// <summary>A single `key&lt;operator&gt;value` search-query comparison against one stored field.</summary>
/// <param name="Operator">The comparison to apply.</param>
/// <param name="Value">The value to compare the stored field against.</param>
public sealed record ComparableFilter<T>(ComparisonOperator Operator, T Value);

/// <summary>
///     A `key&lt;operator&gt;value` comparison against a stored instant, where the query's value may
///     name only a year, a year and month, or a year/month/day -- in which case it names a whole
///     window of time rather than one precise instant.
/// </summary>
/// <param name="Operator">The comparison to apply.</param>
/// <param name="RangeStart">The start of the window the query's value names (inclusive).</param>
/// <param name="RangeEnd">
///     The end of the window the query's value names (exclusive) -- equal to
///     <paramref name="RangeStart" /> when the query gave a precise instant rather than a
///     year/month/day.
/// </param>
/// <remarks>
///     <see cref="ComparisonOperator.Equal" /> matches anywhere inside
///     [<see cref="RangeStart" />, <see cref="RangeEnd" />); <see cref="ComparisonOperator.GreaterThan" />
///     and <see cref="ComparisonOperator.LessThanOrEqual" /> both anchor to
///     <see cref="RangeEnd" /> (strictly after the whole window, or anywhere up through it);
///     <see cref="ComparisonOperator.GreaterThanOrEqual" /> and <see cref="ComparisonOperator.LessThan" />
///     both anchor to <see cref="RangeStart" /> (at or after the window begins, or strictly before it
///     begins).
/// </remarks>
public sealed record DateFilter(ComparisonOperator Operator, DateTimeOffset RangeStart, DateTimeOffset RangeEnd);

/// <summary>
///     A parsed beatmapset search query: free-text keywords plus zero or more structured filters,
///     in the same style as osu!'s own beatmap search syntax (e.g. <c>stars&gt;5 ar=9 artist=camellia</c>).
/// </summary>
/// <remarks>
///     Every filter here maps to a field Basil actually stores. osu!'s own search syntax additionally
///     supports <c>source</c>, <c>tag</c>, <c>favourites</c>, <c>ranked</c> (a separate approval date),
///     <c>divisor</c>, and <c>featured_artist</c> -- none of which Basil has underlying data for (no
///     per-map source/tag metadata, no favourites system, no per-map ranked-status curation, no stored
///     beat-snap divisor). A query using one of those keys degrades gracefully: since it isn't
///     recognized here, it's left in <see cref="Keywords" /> as literal text rather than rejected.
/// </remarks>
/// <param name="Keywords">The free-text portion of the query, matched against artist/title/creator.</param>
/// <param name="Stars">Filters on the beatmap's star rating.</param>
/// <param name="Ar">Filters on the beatmap's approach rate.</param>
/// <param name="Hp">Filters on the beatmap's health drain rate (osu!'s <c>dr</c>/<c>hp</c> keys).</param>
/// <param name="Cs">Filters on the beatmap's circle size.</param>
/// <param name="Od">Filters on the beatmap's overall difficulty.</param>
/// <param name="Bpm">Filters on the beatmap's beats per minute.</param>
/// <param name="LengthSeconds">Filters on the beatmap's total length, in seconds.</param>
/// <param name="Keys">
///     Filters on the beatmap's key count. Aliases <see cref="Cs" />: osu!mania's key count and every
///     other mode's circle size are the same stored field, matching real osu!'s own convention.
/// </param>
/// <param name="Circles">Filters on the beatmap's circle count. Only osu!-mode beatmaps have this.</param>
/// <param name="Sliders">Filters on the beatmap's slider count. Only osu!-mode beatmaps have this.</param>
/// <param name="Creator">The set's creator must match exactly, case-insensitively.</param>
/// <param name="Artist">The set's artist must contain this text, case-insensitively.</param>
/// <param name="Title">The set's title must contain this text, case-insensitively.</param>
/// <param name="Difficulty">The beatmap's difficulty name must contain this text, case-insensitively.</param>
/// <param name="Status">
///     The set's ranked status must equal this value. Every beatmapset on this server reports the same
///     status (see <see cref="Beatmapset.Status" />), so this filter is either a match-everything or
///     match-nothing switch rather than a genuine discriminator.
/// </param>
/// <param name="Created">Filters on when the set was first ingested.</param>
/// <param name="Updated">Filters on when the set was last re-ingested or changed.</param>
public sealed record BeatmapsetSearchFilters(
	string? Keywords = null,
	ComparableFilter<double>? Stars = null,
	ComparableFilter<double>? Ar = null,
	ComparableFilter<double>? Hp = null,
	ComparableFilter<double>? Cs = null,
	ComparableFilter<double>? Od = null,
	ComparableFilter<double>? Bpm = null,
	ComparableFilter<double>? LengthSeconds = null,
	ComparableFilter<double>? Keys = null,
	ComparableFilter<int>? Circles = null,
	ComparableFilter<int>? Sliders = null,
	string? Creator = null,
	string? Artist = null,
	string? Title = null,
	string? Difficulty = null,
	BeatmapStatus? Status = null,
	DateFilter? Created = null,
	DateFilter? Updated = null)
{
	/// <summary>An empty filter set: every beatmapset matches.</summary>
	public static readonly BeatmapsetSearchFilters Empty = new();
}