using Basil.Domain.Beatmaps;

namespace Basil.Application.Queries;

/// <summary>
///     A parsed beatmapset search query: free-text keywords plus zero or more structured filters,
///     in the same style as osu!'s own beatmap search syntax (e.g. <c>artist=camellia status=ranked</c>).
/// </summary>
/// <remarks>
///     The filters here target the set as a whole: its creator/artist/title text, its ranked
///     status, and its ingestion dates. Filters on the beatmaps *inside* a set (stars, ar, cs, od,
///     hp, bpm, length, keys, circles, sliders) live on <see cref="BeatmapQuery" /> instead.
///     osu!'s own search syntax additionally supports <c>source</c>, <c>tag</c>, <c>favourites</c>,
///     <c>ranked</c> (a separate approval date), <c>divisor</c>, and <c>featured_artist</c> -- none
///     of which Basil has underlying data for (no per-map source/tag metadata, no favourites
///     system, no per-map ranked-status curation, no stored beat-snap divisor). A query using one
///     of those keys degrades gracefully: since it isn't recognized here, it's left in
///     <see cref="Keywords" /> as literal text rather than rejected.
/// </remarks>
/// <param name="Keywords">The free-text portion of the query, matched against artist/title/creator.</param>
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
public sealed partial record BeatmapsetQuery(
	string? Keywords = null,
	bool OnlyVisible = true,
	bool OnlyLocked = false,
	string? Creator = null,
	string? Artist = null,
	string? Title = null,
	string? Difficulty = null,
	BeatmapStatus? Status = null,
	DateQuery? Created = null,
	DateQuery? Updated = null) : IParsable<BeatmapsetQuery>, IQuery<Beatmapset>
{
	/// <summary>An empty filter set: every beatmapset matches.</summary>
	public static readonly BeatmapsetQuery Empty = new();
}