using Basil.Application.Beatmaps.Persistence;

namespace Basil.Application.Beatmaps.Ports;

/// <summary>Keeps the beatmapsets the server has in step with the archives in the storage.</summary>
internal interface IBeatmapsetScanner
{
	/// <summary>Brings the server in step with every entry of the storage.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>How many beatmapsets were taken in, updated and forgotten.</returns>
	Task<BeatmapsetScanOutcome> ScanAsync(CancellationToken cancellationToken = default);

	/// <summary>Brings the server in step with one entry of the storage.</summary>
	/// <param name="entry">The entry as it is now.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>How many beatmapsets were taken in, updated and forgotten.</returns>
	Task<BeatmapsetScanOutcome> ScanAsync(BeatmapsetSource entry, CancellationToken cancellationToken = default);
}