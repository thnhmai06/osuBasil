using Basil.Domain.Beatmaps;

namespace Basil.Application.Abstractions.Beatmaps;

/// <summary>
///     Provides beatmap resolution: lookup by md5, id, filename, or set, and upsert of a single
///     difficulty record.
/// </summary>
/// <remarks>
///     Privacy is enforced here at the query level. Every beatmap that sits under a private beatmapset
///     (one whose <see cref="Beatmapset.IsPrivate" /> flag is set) is hidden from every lookup and
///     discovery surface unless the caller explicitly opts in with
///     <c>includePrivate</c>; that flag is set-level, never per-beatmap. The upsert path is the
///     internal ingestion plumbing that must still see those rows to update them in place.
/// </remarks>
public interface IBeatmapRepository
{
	/// <summary>
	///     Looks up a single beatmap by any combination of id, md5, filename, or setId, joined with
	///     its owning beatmapset.
	/// </summary>
	/// <param name="id">The numeric id of the beatmap to find.</param>
	/// <param name="md5">The content md5 of the beatmap to find.</param>
	/// <param name="filename">The on-disk filename of the beatmap to find.</param>
	/// <param name="setId">The id of the set that owns the beatmap to find.</param>
	/// <param name="includePrivate">
	///     <see langword="true" /> to allow matching a beatmap under a private beatmapset; otherwise,
	///     <see langword="false" /> to return only public beatmaps.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The matching beatmap, or <see langword="null" /> when no row matches.</returns>
	/// <remarks>
	///     At least one of id, md5, filename, or setId must be supplied. Id, md5, and filename each
	///     match at most one beatmap; setId can match several difficulties within the same set, in
	///     which case any one of them is returned. The <paramref name="includePrivate" /> default of
	///     <see langword="false" /> matches every public lookup; only callers that must see private
	///     content (for example ingestion reconciliation) pass <see langword="true" />.
	/// </remarks>
	Task<Beatmap?> FetchOneAsync(int? id = null, string? md5 = null, string? filename = null, int? setId = null,
		bool includePrivate = false, CancellationToken cancellationToken = default);

	/// <summary>
	///     Inserts or replaces a single beatmap row and returns the row as actually persisted, with
	///     its resolved id.
	/// </summary>
	/// <param name="beatmap">The beatmap to persist.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The persisted beatmap, with its id resolved and its metadata intact.</returns>
	/// <remarks>
	///     The row is matched by <see cref="Beatmap.Md5" /> first: when a row with that md5 already
	///     exists, its own id is kept regardless of what <paramref name="beatmap" /> carries, so a
	///     re-ingested difficulty never changes identity. Otherwise the id on
	///     <paramref name="beatmap" /> is used directly when it is a positive real osu! online id,
	///     or a fresh local id is allocated from
	///     <c>Math.Max(Beatmap.LocalIdFloor, FetchMaxIdAsync() + 1)</c> when it is 0. The write is a
	///     replace: existing columns are overwritten by the incoming values.
	/// </remarks>
	Task<Beatmap> UpsertAsync(Beatmap beatmap, CancellationToken cancellationToken = default);

	/// <summary>
	///     Deletes the beatmap row with the given content md5.
	/// </summary>
	/// <param name="md5">The content md5 of the beatmap to delete.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task DeleteByMd5Async(string md5, CancellationToken cancellationToken = default);

	/// <summary>
	///     Searches beatmaps locally and returns the matching sets, grouped by setId.
	/// </summary>
	/// <param name="query">A free-text term matched against artist, title, and creator.</param>
	/// <param name="mode">An optional game mode that every returned beatmap must belong to.</param>
	/// <param name="offset">The number of matching sets to skip before returning results.</param>
	/// <param name="amount">The maximum number of sets to return.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     The matching beatmap sets, newest setId first, each with its difficulties ordered by star
	///     rating ascending.
	/// </returns>
	/// <remarks>
	///     This is a fully offline search: the query is matched against stored beatmap metadata
	///     rather than forwarded to a mirror API. Beatmaps under a private beatmapset are always
	///     excluded, since this is a discovery surface, not a specific-record lookup.
	/// </remarks>
	Task<IReadOnlyList<IReadOnlyList<Beatmap>>> SearchAsync(
		string? query, GameMode? mode, int offset, int amount,
		CancellationToken cancellationToken = default);

	/// <summary>
	///     Gets the highest beatmap id currently in use.
	/// </summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The highest id in use, or 0 when nothing is stored.</returns>
	/// <remarks>
	///     Used to allocate local ids for beatmaps whose .osu file carries no real online id, keeping
	///     the allocation above <see cref="Beatmap.LocalIdFloor" /> so it cannot collide with genuine
	///     osu! ids.
	/// </remarks>
	Task<int> FetchMaxIdAsync(CancellationToken cancellationToken = default);

	/// <summary>
	///     Updates the stored star rating of the beatmap with the given id.
	/// </summary>
	/// <param name="id">The id of the beatmap row to update.</param>
	/// <param name="diff">The freshly computed star rating to store.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task UpdateDiffAsync(int id, double diff, CancellationToken cancellationToken = default);

	/// <summary>
	///     Fetches every beatmap belonging to the given set, joined with its owning beatmapset.
	/// </summary>
	/// <param name="setId">The id of the set to read.</param>
	/// <param name="includePrivate">
	///     <see langword="true" /> to include beatmaps under a private beatmapset; otherwise,
	///     <see langword="false" /> to exclude them.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Every beatmap row in the set, in no particular order.</returns>
	/// <remarks>
	///     The default <see langword="false" /> keeps a private set's beatmaps from being read
	///     wholesale; callers that need every difficulty regardless of privacy (for example
	///     ingestion reconciliation's disk-diff) pass <paramref name="includePrivate" /> as
	///     <see langword="true" />.
	/// </remarks>
	Task<IReadOnlyList<Beatmap>> FetchAllBySetIdAsync(int setId, bool includePrivate = false,
		CancellationToken cancellationToken = default);

	/// <summary>
	///     Counts each set's beatmaps in one query, for callers that need only the count (for
	///     example a beatmapset listing page) and would otherwise call
	///     <see cref="FetchAllBySetIdAsync" /> once per set just to read <c>.Count</c>.
	/// </summary>
	/// <param name="setIds">The ids of the sets to count.</param>
	/// <param name="includePrivate">
	///     <see langword="true" /> to include beatmaps under a private beatmapset in the count;
	///     otherwise, <see langword="false" /> to exclude them.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     Each requested set's beatmap count, keyed by set id. A set with zero matching beatmaps
	///     (including one absent from the store entirely) is simply missing from the result rather
	///     than present with a zero count.
	/// </returns>
	Task<IReadOnlyDictionary<int, int>> FetchCountsBySetIdsAsync(IReadOnlyCollection<int> setIds,
		bool includePrivate = false, CancellationToken cancellationToken = default);
}