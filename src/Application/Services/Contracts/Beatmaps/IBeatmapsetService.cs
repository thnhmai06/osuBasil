using Basil.Application.Services.Contracts.Events;
using Basil.Domain.Beatmaps;

namespace Basil.Application.Services.Contracts.Beatmaps;

/// <summary>Imports, deletes and keeps track of the beatmapsets the server has.</summary>
public interface IBeatmapsetService : IEventPublisher<BeatmapsetEvent>
{
	/// <summary>Imports a beatmapset archive, adding or replacing the beatmapset it belongs to.</summary>
	/// <param name="archive">The .osz archive, read from its current position; not disposed.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The result of the import.</returns>
	/// <remarks>
	///     The beatmapset is the stored one that already has a beatmap of the archive; otherwise the stored one with the
	///     id the archive declares; otherwise a new beatmapset, given the declared id when it has one. Difficulties the
	///     new version no longer has are removed. A locked beatmapset is not changed.
	/// </remarks>
	Task<BeatmapsetImportResult> ImportAsync(Stream archive, CancellationToken cancellationToken = default);

	/// <summary>Imports a folder holding a beatmapset's files, adding or replacing the beatmapset it belongs to.</summary>
	/// <param name="folder">The folder; every file under it belongs to the set, and it is left as it is.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The result of the import.</returns>
	/// <remarks>The beatmapset is chosen as for an archive.</remarks>
	Task<BeatmapsetImportResult> ImportAsync(DirectoryInfo folder, CancellationToken cancellationToken = default);

	/// <summary>Deletes a beatmapset with its beatmaps and archive.</summary>
	/// <param name="set">The beatmapset to delete.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> if the beatmapset was deleted; <see langword="false" /> when the beatmapset is locked.</returns>
	Task<bool> DeleteAsync(Beatmapset set, CancellationToken cancellationToken = default);

	/// <summary>Forgets every beatmapset whose archive is no longer stored.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>How many beatmapsets were forgotten.</returns>
	/// <remarks>Run by the host when it starts.</remarks>
	Task<int> ScanAsync(CancellationToken cancellationToken = default);
}