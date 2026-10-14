using Basil.Application.Storage.Contracts.Common;
using Basil.Domain.Beatmaps;

namespace Basil.Application.Storage.Contracts.Beatmaps;

/// <summary>Stores beatmapsets.</summary>
public interface IBeatmapsetRepository
{
	/// <summary>Gets a beatmapset by id.</summary>
	/// <param name="id">The beatmapset id.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The beatmapset, or <see langword="null" /> when none has that id.</returns>
	ValueTask<Beatmapset?> GetAsync(int id, CancellationToken cancellationToken = default);

	/// <summary>Stores a new beatmapset and assigns its id.</summary>
	/// <param name="data">The data of the new beatmapset.</param>
	/// <param name="onlineId">The beatmapset's osu! id, which becomes its id; <see langword="null" /> when it has none.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The stored beatmapset.</returns>
	/// <remarks>
	///     A beatmapset without an osu! id gets a new local id, at or above <see cref="Beatmapset.LocalIdFloor" />. The
	///     caller makes sure no stored beatmapset already has <paramref name="onlineId" />.
	/// </remarks>
	/// <exception cref="AlreadyExistsException">A beatmapset already has <paramref name="onlineId" />.</exception>
	Task<Beatmapset> CreateAsync(BeatmapsetData data, int? onlineId = null,
		CancellationToken cancellationToken = default);

	/// <summary>Stores the new values of a created beatmapset, replacing the stored ones.</summary>
	/// <param name="set">The beatmapset to store.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task UpdateAsync(Beatmapset set, CancellationToken cancellationToken = default);

	/// <summary>Lists the beatmapsets that have at least one beatmap the query includes, newest id first.</summary>
	/// <param name="query">Which beatmaps to include.</param>
	/// <param name="page">Which part of the listing to return.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>A page of beatmapsets.</returns>
	Task<Page<Beatmapset>> ListAsync(BeatmapQuery query, PageRequest page,
		CancellationToken cancellationToken = default);

	/// <summary>Brings the stored beatmapsets and their beatmaps in step with what was read, all at once.</summary>
	/// <param name="sync">The beatmapsets to store and to remove.</param>
	/// <param name="cancellationToken">A token that cancels the wait for its turn.</param>
	/// <returns>The stored beatmapsets, in the order of <see cref="BeatmapsetSync.Store" />.</returns>
	/// <remarks>
	///     Either every change is stored or none is. A replaced beatmap keeps its id and whether it is locked or
	///     visible; a replaced set keeps its id, creation time, lock and visibility. New sets and beatmaps get their
	///     osu! id, or a new local id. The live instances in memory are changed only once everything is stored.
	/// </remarks>
	/// <exception cref="AlreadyExistsException">A new set or beatmap has an id or hash another stored one has.</exception>
	Task<IReadOnlyList<Beatmapset>> SyncAsync(BeatmapsetSync sync, CancellationToken cancellationToken = default);

	/// <summary>Deletes a beatmapset.</summary>
	/// <remarks>Remove the set's beatmaps first, with <see cref="IBeatmapRepository.RetainAsync" /> keeping none.</remarks>
	/// <param name="set">The beatmapset to delete.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task DeleteAsync(Beatmapset set, CancellationToken cancellationToken = default);
}