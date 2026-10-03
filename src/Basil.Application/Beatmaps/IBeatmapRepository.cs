using Basil.Application.Common;
using Basil.Domain.Beatmaps;
using Basil.Domain.Utilities;

namespace Basil.Application.Beatmaps;

/// <summary>Stores beatmap difficulties.</summary>
public interface IBeatmapRepository
{
	/// <summary>Stores a beatmap under its id, adding it when no beatmap has that id and replacing the stored beatmap otherwise.</summary>
	/// <param name="beatmap">The beatmap to store.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
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
	ValueTask<Beatmap?> GetByHashAsync(Md5 hash, CancellationToken cancellationToken = default);

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