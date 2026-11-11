using Basil.Application.Shared;
using Basil.Application.Shared.Exceptions;
using Basil.Application.Shared.Paging;
using Basil.Domain.Beatmaps;
using Basil.Domain.Shared;
using Basil.Domain.Cryptography;

namespace Basil.Application.Beatmaps.Persistence;

/// <summary>Stores beatmap difficulties.</summary>
public interface IBeatmapRepository
{
	/// <summary>Stores the new values of a created beatmap, replacing the stored ones.</summary>
	/// <param name="beatmap">The beatmap to store.</param>
	/// <remarks>The values are stored when the call returns; a beatmap read afterwards has them.</remarks>
	/// <exception cref="AlreadyExistsException">Another beatmap already has the beatmap's hash.</exception>
	Task UpdateAsync(BeatmapRecord beatmap);

	/// <summary>Gets a beatmap by id.</summary>
	/// <param name="id">The beatmap id.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The beatmap, or <see langword="null" /> when none has that id.</returns>
	ValueTask<BeatmapRecord?> GetAsync(int id, CancellationToken ct = default);

	/// <summary>Gets a beatmap by the MD5 of its file.</summary>
	/// <param name="hash">The MD5 hash of the beatmap file.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The beatmap, or <see langword="null" /> when none has that hash.</returns>
	ValueTask<BeatmapRecord?> GetAsync(Md5 hash, CancellationToken ct = default);

	/// <summary>Lists the beatmaps of a beatmapset, ordered by id.</summary>
	/// <remarks>At most <see cref="ListLimits.Ceiling" /> beatmaps are returned.</remarks>
	/// <param name="set">The beatmapset.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The beatmaps of the set.</returns>
	Task<IReadOnlyList<BeatmapRecord>> ListAsync(BeatmapsetRecord set, CancellationToken ct = default);

	/// <summary>Lists the beatmaps a query includes, ordered by beatmapset id descending, then by star rating.</summary>
	/// <param name="query">Which beatmaps to include.</param>
	/// <param name="page">Which part of the listing to return.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>A page of beatmaps.</returns>
	Task<Page<BeatmapRecord>> ListAsync(BeatmapQuery query, PageRequest page,
		CancellationToken ct = default);

	/// <summary>Deletes the stored beatmaps of a beatmapset that are not in a given list.</summary>
	/// <param name="set">The beatmapset.</param>
	/// <param name="keep">The beatmaps the set still has.</param>
	Task RetainAsync(BeatmapsetRecord set, IReadOnlyCollection<BeatmapRecord> keep);
}