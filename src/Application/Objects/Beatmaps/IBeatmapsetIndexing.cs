using Basil.Application.Storage.Beatmaps;

namespace Basil.Application.Objects.Beatmaps;

/// <summary>Indexes beatmapset archives and reports the records changed by each operation.</summary>
internal interface IBeatmapsetIndexing
{
	/// <summary>Imports one beatmapset archive.</summary>
	Task<(BeatmapsetImportResult Result, BeatmapsetIndexed? Imported)> ImportAsync(Stream archive,
		CancellationToken cancellationToken = default);

	/// <summary>Scans every beatmapset source.</summary>
	Task<BeatmapsetScanOutcome> ScanAsync(CancellationToken cancellationToken = default);

	/// <summary>Scans one changed beatmapset source.</summary>
	Task<BeatmapsetScanOutcome> ScanAsync(BeatmapsetSource entry, CancellationToken cancellationToken = default);
}
