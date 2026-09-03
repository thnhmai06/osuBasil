using Basil.Domain.Beatmaps;

namespace Basil.Application.Abstractions.Beatmaps;

/// <summary>
///     Provides set-level access to stored beatmap sets.
/// </summary>
/// <remarks>
///     A beatmap set is a first-class record here, holding the fields that every difficulty shares
///     (artist, title, creator, timestamps) plus the admin-controlled visibility and write-lock
///     flags. <c>BeatmapIngestionService</c> and <c>BeatmapWatcherService</c> are the only writers;
///     the read paths are beatmap resolution and set listing.
/// </remarks>
public interface IBeatmapsetRepository
{
	/// <summary>
	///     Fetches the beatmapset with the given id.
	/// </summary>
	/// <param name="id">The id of the set to find.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The matching beatmapset, or <see langword="null" /> when no such row exists.</returns>
	Task<Beatmapset?> FetchByIdAsync(int id, CancellationToken cancellationToken = default);

	/// <summary>
	///     Inserts or updates a beatmapset row and returns the row as persisted.
	/// </summary>
	/// <param name="beatmapset">The beatmapset to persist.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The persisted beatmapset.</returns>
	/// <remarks>
	///     The write is an insert-or-update on the set's id, not a replace: on a conflict only the
	///     shared metadata fields (artist, title, creator, and timestamps) are overwritten. The
	///     <see cref="Beatmapset.IsFrozen" />, <see cref="Beatmapset.IsPrivate" />, and media-file columns are
	///     deliberately left untouched by a re-ingestion, so a reconcile pass can never clear an
	///     admin-set freeze lock or privacy flag, and it cannot yet know the pass's actual
	///     background/audio files (those are set later via
	///     <see cref="SetBackgroundFileAsync" />/<see cref="SetAudioFileAsync" />).
	/// </remarks>
	Task<Beatmapset> UpsertAsync(Beatmapset beatmapset, CancellationToken cancellationToken = default);

	/// <summary>
	///     Deletes the beatmapset with the given id and every beatmap it owns.
	/// </summary>
	/// <param name="id">The id of the set to delete.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>
	///     The deletion also removes every beatmap the set owns, so no separate cleanup is needed.
	/// </remarks>
	Task DeleteAsync(int id, CancellationToken cancellationToken = default);

	/// <summary>
	///     Gets the highest set id currently in use.
	/// </summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The highest id in use, or 0 when nothing is stored.</returns>
	/// <remarks>
	///     Used to allocate local set ids for ingested sets that have no real osu! online id.
	/// </remarks>
	Task<int> FetchMaxIdAsync(CancellationToken cancellationToken = default);

	/// <summary>
	///     Fetches every beatmapset id currently stored.
	/// </summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Every beatmapset id in the database.</returns>
	/// <remarks>
	///     Used by the full reconciliation pass to find sets whose backing folder no longer exists
	///     on disk.
	/// </remarks>
	Task<IReadOnlyList<int>> FetchAllIdsAsync(CancellationToken cancellationToken = default);

	/// <summary>
	///     Fetches a page of beatmapsets, newest id first.
	/// </summary>
	/// <param name="offset">The number of sets to skip before returning results.</param>
	/// <param name="limit">The maximum number of sets to return.</param>
	/// <param name="onlyWithVisibleBeatmaps">
	///     <see langword="true" /> to exclude sets whose <see cref="Beatmapset.IsPrivate" /> flag is set;
	///     otherwise, <see langword="false" /> to return every set regardless of privacy.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The requested page of beatmapsets.</returns>
	/// <remarks>
	///     Pass <paramref name="onlyWithVisibleBeatmaps" /> as <see langword="true" /> for a
	///     public, non-admin caller, who sees only non-private sets, and <see langword="false" />
	///     for an admin-elevated caller, who sees every set regardless.
	/// </remarks>
	Task<IReadOnlyList<Beatmapset>> FetchPageAsync(int offset, int limit, bool onlyWithVisibleBeatmaps,
		CancellationToken cancellationToken = default);

	/// <summary>
	///     Sets or clears the beatmapset's write-lock flag.
	/// </summary>
	/// <param name="id">The id of the set to update.</param>
	/// <param name="frozen">
	///     <see langword="true" /> to lock the set against further updates; otherwise,
	///     <see langword="false" /> to unlock it.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>
	///     While the flag is set, updates and deletions of the set are rejected until it is cleared
	///     again. The freeze toggle itself stays exempt from its own lock.
	/// </remarks>
	Task SetFrozenAsync(int id, bool frozen, CancellationToken cancellationToken = default);

	/// <summary>
	///     Sets or clears the beatmapset's privacy flag, hiding every beatmap under the set from
	///     non-admin listings and lookups.
	/// </summary>
	/// <param name="id">The id of the set to update.</param>
	/// <param name="isPrivate">
	///     <see langword="true" /> to hide the set from non-admin callers; otherwise,
	///     <see langword="false" /> to make it publicly visible.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>
	///     This is the set-level visibility flag enforced across every beatmap lookup and discovery
	///     surface.
	/// </remarks>
	Task SetPrivateAsync(int id, bool isPrivate, CancellationToken cancellationToken = default);

	/// <summary>
	///     Sets the background image file name of the given beatmapset.
	/// </summary>
	/// <param name="id">The id of the set to update.</param>
	/// <param name="backgroundFile">The background file name to store, or <see langword="null" />.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>
	///     Called by <c>BeatmapIngestionService.ReconcileFolderAsync</c> once the set's beatmaps for
	///     that pass are known; the value is the lowest-id beatmap's background file in the set.
	/// </remarks>
	Task SetBackgroundFileAsync(int id, string? backgroundFile, CancellationToken cancellationToken = default);

	/// <summary>
	///     Sets the audio file name of the given beatmapset.
	/// </summary>
	/// <param name="id">The id of the set to update.</param>
	/// <param name="audioFile">The audio file name to store, or <see langword="null" />.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>
	///     Mirrors <see cref="SetBackgroundFileAsync" />: called by
	///     <c>BeatmapIngestionService.ReconcileFolderAsync</c> the same way, storing the lowest-id
	///     beatmap's audio file in the set.
	/// </remarks>
	Task SetAudioFileAsync(int id, string? audioFile, CancellationToken cancellationToken = default);

	/// <summary>
	///     Gets the total number of beatmapsets in the database.
	/// </summary>
	/// <param name="includePrivate">
	///     <see langword="true" /> to count every beatmapset including private ones; otherwise,
	///     <see langword="false" /> to count only public sets.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The requested beatmapset count.</returns>
	/// <remarks>
	///     The <paramref name="includePrivate" /> flag mirrors <see cref="FetchPageAsync" />'s
	///     <c>onlyWithVisibleBeatmaps</c> argument, inverted: pass <see langword="false" /> for a
	///     non-admin caller.
	/// </remarks>
	Task<int> FetchCountAsync(bool includePrivate, CancellationToken cancellationToken = default);
}