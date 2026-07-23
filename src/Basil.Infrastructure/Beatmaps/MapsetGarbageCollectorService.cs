using Basil.Application.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Beatmaps;

/// <summary>
///     Physically deletes <see cref="BeatmapIngestionService.DeletedFolderInfix" />-marked mapset
///     folders — the atomic in-place rename the `api.` host's async mapset-delete route performs
///     (and <see cref="BeatmapWatcherService" />/<see cref="BeatmapIngestionService" /> already treat
///     as "gone" for DB purposes) leaves the folder itself on disk until this pass reclaims it. Runs
///     on its own timer (not driven by the live <see cref="FileSystemWatcher" />) so a locked file
///     (an in-flight read from another process) just gets retried next cycle instead of failing the
///     delete route or the watcher's debounce.
/// </summary>
public sealed class MapsetGarbageCollectorService(
    IOptions<StorageOptions> options,
    ILogger<MapsetGarbageCollectorService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

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
            }
            catch (Exception e)
            {
                // ponytail: a locked file (in-flight read elsewhere) just retries next cycle instead
                // of aborting the whole pass.
                logger.LogWarning(e, "Failed to garbage-collect deleted mapset folder {Path}; will retry next cycle.",
                    folder);
            }
        }
    }
}
