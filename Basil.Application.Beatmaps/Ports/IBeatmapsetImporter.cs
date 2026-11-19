using Basil.Application.Beatmaps.Persistence;

namespace Basil.Application.Beatmaps.Ports;

internal interface IBeatmapsetImporter
{
	Task<(BeatmapsetImportResult Result, BeatmapsetIndexed? Imported)> ImportAsync(Stream archive,
		bool replaceExisting, CancellationToken ct = default);
}