using Basil.Application.Storage.Contracts.Common;
using Basil.Domain.Beatmaps;
using Basil.Domain.Utilities;

namespace Basil.Application.Storage.Contracts.Beatmaps;

/// <summary>Stores beatmap difficulties.</summary>
public interface IBeatmapRepository
{
	/// <summary>Stores a new beatmap and assigns its id.</summary>
	/// <param name="data">The data of the new beatmap.</param>
	/// <param name="onlineId">The beatmap's osu! id, which becomes its id; <see langword="null" /> when it has none.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The stored beatmap.</returns>
	/// <remarks>
	///     A beatmap without an osu! id gets a new local id, at or above <see cref="Beatmap.LocalIdFloor" />. The caller
	///     makes sure no stored beatmap already has <paramref name="onlineId" /> or the same file hash.
	/// </remarks>
	Task<Beatmap> CreateAsync(BeatmapData data, int? onlineId = null, CancellationToken cancellationToken = default);

	/// <summary>
	///     Stores a beatmap under its id, adding it when no beatmap has that id and replacing the stored beatmap
	///     otherwise.
	/// </summary>
	/// <param name="beatmap">The beatmap to store.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>A beatmap already stored keeps its identity: the instance the repository returns for it takes the new data.</remarks>
	/// <exception cref="InvalidOperationException">Another beatmap already has the beatmap's hash.</exception>
	Task CreateOrUpdateAsync(Beatmap beatmap, CancellationToken cancellationToken = default);

	/// <summary>Gets a beatmap by id.</summary>
	/// <param name="id">The beatmap id.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The beatmap, or <see langword="null" /> when none has that id.</returns>
	ValueTask<Beatmap?> GetAsync(int id, CancellationToken cancellationToken = default);

	/// <summary>Gets a beatmap by the MD5 of its file.</summary>
	/// <param name="hash">The MD5 hash of the beatmap file.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The beatmap, or <see langword="null" /> when none has that hash.</returns>
	ValueTask<Beatmap?> GetAsync(Md5 hash, CancellationToken cancellationToken = default);

	/// <summary>Lists the beatmaps of a beatmapset, ordered by id.</summary>
	/// <param name="set">The beatmapset.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The beatmaps of the set.</returns>
	Task<IReadOnlyList<Beatmap>> ListAsync(Beatmapset set, CancellationToken cancellationToken = default);

	/// <summary>Lists the beatmaps a query includes, ordered by beatmapset id descending, then by star rating.</summary>
	/// <param name="query">Which beatmaps to include.</param>
	/// <param name="page">Which part of the listing to return.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>A page of beatmaps.</returns>
	Task<Page<Beatmap>> ListAsync(BeatmapQuery query, PageRequest page, CancellationToken cancellationToken = default);

	/// <summary>Deletes the stored beatmaps of a beatmapset that are not in a given list.</summary>
	/// <param name="set">The beatmapset.</param>
	/// <param name="keep">The beatmaps the set still has.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task RetainAsync(Beatmapset set, IReadOnlyCollection<Beatmap> keep, CancellationToken cancellationToken = default);
}