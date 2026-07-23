using System.Collections.Concurrent;
using Basil.Application.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Beatmaps;

/// <summary>
///     Live-syncs the DB with <see cref="StorageOptions.MapsetsPath" /> after startup: any create/
///     change/delete/rename under that folder is debounced (a dragged-in mapset fires many rapid
///     events for the same folder — only the last one after a quiet period actually reconciles) and
///     handed to <see cref="BeatmapIngestionService" />'s per-folder methods. The one-time full pass
///     (<see cref="BeatmapIngestionService.ReconcileAllAsync" />) runs separately in Program.cs
///     before the host starts, so there's no duplicate scan or race with this service's first events.
/// </summary>
public sealed class BeatmapWatcherService(
    BeatmapIngestionService ingestion,
    IOptions<StorageOptions> options,
    ILogger<BeatmapWatcherService> logger) : BackgroundService
{
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromSeconds(2);
    private readonly ConcurrentDictionary<string, Timer> _timers = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var path = options.Value.MapsetsPath;
        Directory.CreateDirectory(path);

        using var watcher = new FileSystemWatcher(path);
        watcher.IncludeSubdirectories = true;
        watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite;
        watcher.Created += (_, e) => Debounce(AffectedPath(path, e.FullPath));
        watcher.Changed += (_, e) => Debounce(AffectedPath(path, e.FullPath));
        watcher.Renamed += (_, e) => DebounceRenamed(path, e);
        watcher.Deleted += (_, e) => Debounce(AffectedPath(path, e.FullPath));
        watcher.Error += (_, e) => logger.LogWarning(e.GetException(), "Mapsets FileSystemWatcher error.");
        watcher.EnableRaisingEvents = true;

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }

        foreach (var timer in _timers.Values) await timer.DisposeAsync();
    }

    /// <summary>Resolves any changed path back to the top-level entry directly under MapsetsPath it belongs to.</summary>
    private static string? AffectedPath(string root, string fullPath)
    {
        var relative = Path.GetRelativePath(root, fullPath);
        if (relative == "." || relative.StartsWith("..", StringComparison.Ordinal)) return null;

        var firstSegment = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return Path.Combine(root, firstSegment);
    }

    /// <summary>
    ///     A rename into a `.deleted_`-suffixed name (see
    ///     <see cref="BeatmapIngestionService.DeletedFolderInfix" /> — the atomic marker the `api.`
    ///     host's async mapset-delete route uses) means the folder's *new* name is never a live
    ///     mapset. Debouncing on the *old* path instead lets <see cref="Settle" />'s own
    ///     Directory.Exists/File.Exists checks naturally resolve it to
    ///     <see cref="BeatmapIngestionService.ReconcileDeletedFolderAsync" /> — the old path no longer
    ///     exists on disk, and its name (unlike the new one) still carries the mapset's real leading
    ///     id. Any other rename (e.g. a human renaming a mapset folder) still debounces on the new
    ///     path as before.
    /// </summary>
    private void DebounceRenamed(string root, RenamedEventArgs e)
    {
        var newAffected = AffectedPath(root, e.FullPath);
        if (newAffected is not null && Path.GetFileName(newAffected)
                .Contains(BeatmapIngestionService.DeletedFolderInfix, StringComparison.OrdinalIgnoreCase))
        {
            Debounce(AffectedPath(root, e.OldFullPath));
            return;
        }

        Debounce(newAffected);
    }

    private void Debounce(string? affected)
    {
        if (affected is null) return;

        _timers.AddOrUpdate(affected,
            _ => new Timer(_ => _ = Settle(affected), null, DebounceWindow, Timeout.InfiniteTimeSpan),
            (_, existing) =>
            {
                existing.Change(DebounceWindow, Timeout.InfiniteTimeSpan);
                return existing;
            });
    }

    private async Task Settle(string affected)
    {
        _timers.TryRemove(affected, out var timer);
        if (timer is not null) await timer.DisposeAsync();

        // A `.deleted_` folder is mid-deletion (rename-in-place done, physical removal pending the
        // GC pass) — never a live mapset, so no reconciliation pathway applies to it.
        if (Path.GetFileName(affected).Contains(BeatmapIngestionService.DeletedFolderInfix, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            if (Directory.Exists(affected))
                await ingestion.ReconcileFolderAsync(affected);
            else if (File.Exists(affected) && affected.EndsWith(".osz", StringComparison.OrdinalIgnoreCase))
                await ingestion.ReconcileOszAsync(affected);
            else if (!File.Exists(affected))
                await ingestion.ReconcileDeletedFolderAsync(affected);
            // else: a stray non-.osz file at the root — no valid ingestion pathway, ignore.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to reconcile Mapsets path {Path} after a filesystem change.", affected);
        }
    }
}
