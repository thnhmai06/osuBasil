using Basil.Application.Beatmaps.Persistence;

namespace Basil.Application.Beatmaps.Ports;

/// <summary>Takes one uploaded beatmapset archive into the server.</summary>
internal interface IBeatmapsetImporter
{
	/// <summary>Imports one uploaded archive as a beatmapset.</summary>
	/// <param name="archive">The uploaded archive.</param>
	/// <param name="replaceExisting">
	///     Whether an archive of a beatmapset the server already has replaces it; otherwise it is rejected.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The result of the import and the beatmapset taken in, or <see langword="null" /> when it was rejected.</returns>
	Task<(BeatmapsetImportResult Result, BeatmapsetIndexed? Imported)> ImportAsync(Stream archive,
		bool replaceExisting, CancellationToken cancellationToken = default);
}