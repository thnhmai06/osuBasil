using Basil.Application.Configurations;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Beatmaps;

/// <summary>
///     Physically deletes beatmapset folders whose name carries
///     <see cref="BeatmapIngestionService.DeletedFolderInfix" />. The rename that marks a folder for
///     deletion leaves it on disk until this pass reclaims it, and the DB already treats it as gone.
///     The pass runs on a recurring schedule, so a folder that can't be deleted yet (for example a
///     locked file) is simply retried on the next pass.
/// </summary>
public sealed class MapsetGarbageCollectorService(
	IOptions<StorageOptions> options,
	ILogger<MapsetGarbageCollectorService> logger) : BackgroundService
{
	private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

	/// <summary>
	///     Runs the collection loop until shutdown: one immediate pass, then another every
	///     <see cref="Interval" />.
	/// </summary>
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			CollectOnce();

			try
			{
				await Task.Delay(Interval, stoppingToken);
			}
			catch (OperationCanceledException)
			{
				// expected on shutdown
			}
		}
	}

	/// <summary>
	///     Deletes every folder under <see cref="StorageOptions.MapsetsPath" /> whose name carries the
	///     deletion marker. A folder that fails to delete (typically a locked file) is logged and
	///     retried on the next cycle.
	/// </summary>
	private void CollectOnce()
	{
		var path = options.Value.MapsetsPath;
		if (!Directory.Exists(path)) return;

		foreach (var folder in Directory.EnumerateDirectories(path))
		{
			if (!Path.GetFileName(folder)
				    .Contains(BeatmapIngestionService.DeletedFolderInfix, StringComparison.OrdinalIgnoreCase))
				continue;

			try
			{
				Directory.Delete(folder, true);
				logger.LogInformation("- Beatmapset folder physically removed: {Path}", folder);
			}
			catch (Exception e)
			{
				// A locked file (in-flight read elsewhere) just retries next cycle instead of
				// aborting the whole pass.
				logger.LogWarning(e,
					"Failed to garbage-collect deleted beatmapset folder {Path}; will retry next cycle.",
					folder);
			}
		}
	}
}