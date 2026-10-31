using Basil.Application.Common.Common;
using Basil.Domain.Beatmaps;

namespace Basil.Application.Beatmaps.Persistence;

/// <summary>Stores beatmapsets.</summary>
public interface IBeatmapsetRepository
{
	/// <summary>Gets a beatmapset by id.</summary>
	/// <param name="id">The beatmapset id.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The beatmapset, or <see langword="null" /> when none has that id.</returns>
	ValueTask<BeatmapsetRecord?> GetAsync(int id, CancellationToken cancellationToken = default);

	/// <summary>Stores the new values of a created beatmapset, replacing the stored ones.</summary>
	/// <param name="set">The beatmapset to store.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task UpdateAsync(BeatmapsetRecord set, CancellationToken cancellationToken = default);

	/// <summary>Lists the beatmapsets that have at least one beatmap the query includes, newest id first.</summary>
	/// <param name="query">Which beatmaps to include.</param>
	/// <param name="page">Which part of the listing to return.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>A page of beatmapsets.</returns>
	Task<Page<BeatmapsetRecord>> ListAsync(BeatmapQuery query, PageRequest page,
		CancellationToken cancellationToken = default);

	/// <summary>Stores what one scan or import changes, all at once.</summary>
	/// <param name="changes">The beatmapsets to store and to remove.</param>
	/// <param name="cancellationToken">A token that cancels the wait for its turn.</param>
	/// <returns>The stored beatmapsets, in the order of <see cref="BeatmapsetChanges.Store" />.</returns>
	/// <remarks>
	///     Either every change is stored or none is. A replaced beatmap keeps its id and whether it is locked or
	///     visible; a replaced set keeps its id, creation time, lock and visibility. New sets and beatmaps get their
	///     osu! id, or a new local id. The live instances in memory are changed only once everything is stored.
	/// </remarks>
	/// <exception cref="AlreadyExistsException">A new set or beatmap has an id or hash another stored one has.</exception>
	Task<IReadOnlyList<BeatmapsetRecord>> CommitAsync(BeatmapsetChanges changes,
		CancellationToken cancellationToken = default);

	/// <summary>Deletes a beatmapset.</summary>
	/// <remarks>Remove the set's beatmaps first, with <see cref="IBeatmapRepository.RetainAsync" /> keeping none.</remarks>
	/// <param name="set">The beatmapset to delete.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task DeleteAsync(BeatmapsetRecord set, CancellationToken cancellationToken = default);
}