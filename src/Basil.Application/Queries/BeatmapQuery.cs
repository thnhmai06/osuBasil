using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;

namespace Basil.Application.Queries;

/// <summary>
///     A parsed beatmap search query: free-text keywords plus zero or more structured filters on
///     individual beatmap fields, in the same style as osu!'s own beatmap search syntax
///     (e.g. <c>stars&gt;5 ar=9 keys=7</c>), alongside exact-match identifiers for looking up
///     specific beatmaps.
/// </summary>
/// <remarks>
///     Every filter here maps to a field Basil actually stores on a single beatmap. The set-level
///     filters (artist/title/creator/status and the created/updated dates) live on
///     <see cref="BeatmapsetQuery" /> instead. A query using a key this parser doesn't recognize --
///     either a set-level key, a genuine typo, or one of osu!'s keys Basil has no underlying data
///     for -- degrades gracefully: since it isn't recognized here, it's left in
///     <see cref="Keywords" /> as literal text rather than rejected.
/// </remarks>
/// <param name="Md5">The beatmap's md5 must equal one of these values (an OR match).</param>
/// <param name="Id">The beatmap's id must equal one of these values (an OR match).</param>
/// <param name="BeatmapsetId">The beatmap must belong to one of these sets (an OR match).</param>
/// <param name="GameMode">The beatmap must be one of these game modes (an OR match).</param>
/// <param name="OnlyVisible">When true, private/non-visible beatmaps are allowed to match.</param>
/// <param name="Keywords">The free-text portion of the query, matched as literal search text.</param>
/// <param name="Bpm">Filters on the beatmap's beats per minute.</param>
/// <param name="Length">Filters on the beatmap's total length, in seconds.</param>
/// <param name="Cs">Filters on the beatmap's circle size.</param>
/// <param name="Ar">Filters on the beatmap's approach rate.</param>
/// <param name="Od">Filters on the beatmap's overall difficulty.</param>
/// <param name="Hp">Filters on the beatmap's health drain rate (osu!'s <c>dr</c>/<c>hp</c> keys).</param>
/// <param name="Star">Filters on the beatmap's star rating.</param>
/// <param name="Keys">
///     Filters on the beatmap's key count. Aliases <see cref="Cs" />: osu!mania's key count and every
///     other mode's circle size are the same stored field, matching real osu!'s own convention.
/// </param>
/// <param name="Circles">Filters on the beatmap's circle count. Only osu!-mode beatmaps have this.</param>
/// <param name="Sliders">Filters on the beatmap's slider count. Only osu!-mode beatmaps have this.</param>
public sealed partial record BeatmapQuery(
	string? Keywords = null,
	bool OnlyVisible = true,
	bool OnlyLocked = false,
	IReadOnlyCollection<int>? BeatmapsetId = null,
	IReadOnlyCollection<string>? Md5 = null,
	IReadOnlyCollection<int>? Id = null,
	IReadOnlyCollection<GameMode>? GameMode = null,
	ComparableFilter<double>? Bpm = null,
	ComparableFilter<double>? Length = null,
	ComparableFilter<double>? Cs = null,
	ComparableFilter<double>? Ar = null,
	ComparableFilter<double>? Od = null,
	ComparableFilter<double>? Hp = null,
	ComparableFilter<double>? Star = null,
	ComparableFilter<double>? Keys = null,
	ComparableFilter<int>? Circles = null,
	ComparableFilter<int>? Sliders = null,
	DateQuery? Created = null,
	DateQuery? Updated = null) : IParsable<BeatmapQuery>, IQuery<Beatmap>
{
	/// <summary>An empty filter set: every beatmap matches.</summary>
	public static readonly BeatmapQuery Empty = new();
}