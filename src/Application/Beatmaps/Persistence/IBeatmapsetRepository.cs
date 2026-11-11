using Basil.Application.Shared.Exceptions;
using Basil.Application.Shared.Paging;
using Basil.Domain.Beatmaps;

namespace Basil.Application.Beatmaps.Persistence;

/// <summary>Stores beatmapsets.</summary>
public interface IBeatmapsetRepository
{
	/// <summary>Gets a beatmapset by id.</summary>
	/// <param name="id">The beatmapset id.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The beatmapset, or <see langword="null" /> when none has that id.</returns>
	ValueTask<BeatmapsetRecord?> GetAsync(int id, CancellationToken ct = default);

	/// <summary>Stores the new values of a created beatmapset, replacing the stored ones.</summary>
	/// <param name="set">The beatmapset to store.</param>
	Task UpdateAsync(BeatmapsetRecord set);

	/// <summary>Lists the beatmapsets that have at least one beatmap the query includes, newest id first.</summary>
	/// <param name="query">Which beatmaps to include.</param>
	/// <param name="page">Which part of the listing to return.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>A page of beatmapsets.</returns>
	Task<Page<BeatmapsetRecord>> ListAsync(BeatmapQuery query, PageRequest page,
		CancellationToken ct = default);

	/// <summary>Lists the ids of the stored beatmapsets, newest first.</summary>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The ids.</returns>
	/// <remarks>At most <see cref="ListLimits.Ceiling" /> ids are returned.</remarks>
	Task<IReadOnlyList<int>> ListIdsAsync(CancellationToken ct = default);

	/// <summary>Stores what one scan or import changes, all at once.</summary>
	/// <param name="changes">The beatmapsets to store and to remove.</param>
	/// <returns>The stored beatmapsets, in the order of <see cref="BeatmapsetChanges.Store" />.</returns>
	/// <remarks>
	///     Either every change is stored or none is. A replaced beatmap keeps its id and whether it is locked or
	///     visible; a replaced set keeps its id, creation time, lock and visibility. New sets and beatmaps get their
	///     osu! id, or a new local id.
	/// </remarks>
	/// <exception cref="AlreadyExistsException">A new set or beatmap has an id or hash another stored one has.</exception>
	Task<IReadOnlyList<BeatmapsetRecord>> CommitAsync(BeatmapsetChanges changes);

	/// <summary>Deletes a beatmapset.</summary>
	/// <remarks>Remove the set's beatmaps first, with <see cref="IBeatmapRepository.RetainAsync" /> keeping none.</remarks>
	/// <param name="set">The beatmapset to delete.</param>
	Task DeleteAsync(BeatmapsetRecord set);
}