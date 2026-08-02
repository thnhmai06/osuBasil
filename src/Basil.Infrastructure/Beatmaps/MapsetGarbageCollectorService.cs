using Basil.Application.Configurations;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Beatmaps;

/// <summary>
///     Physically deletes <see cref="BeatmapIngestionService.DeletedFolderInfix" />-marked beatmapset
///     folders. The atomic in-place rename that marks a folder for deletion leaves the folder itself
///     on disk until this pass reclaims it: <see cref="BeatmapWatcherService" /> and
///     <see cref="BeatmapIngestionService" /> already treat such folders as gone for DB purposes.
///     Runs on its own timer (not driven by the live <see cref="FileSystemWatcher" />) so a locked
///     file (an in-flight read from another process) just gets retried next cycle instead of failing
///     that cycle's pass.
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
	///     deletion marker, logging a warning for any that fail (typically a locked file) so they
	///     retry next cycle.
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
				logger.LogWarning(e, "Failed to garbage-collect deleted beatmapset folder {Path}; will retry next cycle.",
					folder);
			}
		}
	}
}