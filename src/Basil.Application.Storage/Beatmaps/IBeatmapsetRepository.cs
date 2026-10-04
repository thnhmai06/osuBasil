using Basil.Application.Storage.Common;
using Basil.Domain.Beatmaps;

namespace Basil.Application.Storage.Beatmaps;

/// <summary>Stores beatmapsets.</summary>
public interface IBeatmapsetRepository
{
	/// <summary>Gets a beatmapset by id.</summary>
	/// <param name="id">The beatmapset id.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The beatmapset, or <see langword="null" /> when none has that id.</returns>
	ValueTask<Beatmapset?> GetAsync(int id, CancellationToken cancellationToken = default);

	/// <summary>
	///     Stores a beatmapset under its id, adding it when no beatmapset has that id and replacing the stored beatmapset
	///     otherwise.
	/// </summary>
	/// <param name="set">The beatmapset to store.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task CreateOrUpdateAsync(Beatmapset set, CancellationToken cancellationToken = default);

	/// <summary>Lists the beatmapsets that have at least one beatmap the query includes, newest id first.</summary>
	/// <param name="query">Which beatmaps to include.</param>
	/// <param name="page">Which part of the listing to return.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>A page of beatmapsets.</returns>
	Task<Page<Beatmapset>> ListAsync(BeatmapQuery query, PageRequest page,
		CancellationToken cancellationToken = default);

	/// <summary>Deletes a beatmapset together with its beatmaps.</summary>
	/// <param name="set">The beatmapset to delete.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task DeleteAsync(Beatmapset set, CancellationToken cancellationToken = default);
}