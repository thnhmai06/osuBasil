using System.Collections.Concurrent;
using Basil.Application.Configurations;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Beatmaps;

/// <summary>
///     Live-syncs the DB with <see cref="StorageOptions.MapsetsPath" /> after startup: any creation,
///     change, delete, or rename under that folder is debounced (a dragged-in beatmapset fires many
///     rapid events for the same folder, so only the last one after a quiet period actually
///     reconciles) and handed to <see cref="BeatmapIngestionService" />'s per-folder methods. The
///     one-time full pass (<see cref="BeatmapIngestionService.ReconcileAllAsync" />) is a separate
///     sweep of the whole storage root, so there is no duplicate scan or race with this service's
///     first events.
/// </summary>
public sealed class BeatmapWatcherService(
	BeatmapIngestionService ingestion,
	IOptions<StorageOptions> options,
	ILogger<BeatmapWatcherService> logger) : BackgroundService
{
	/// <summary>Quiet period after the last filesystem event for a path before it reconciles.</summary>
	private static readonly TimeSpan DebounceWindow = TimeSpan.FromSeconds(2);

	/// <summary>Tracks reconciliation tasks still in flight, so shutdown waits for them.</summary>
	private readonly ConcurrentDictionary<Task, byte> _inFlightSettles = new();

	/// <summary>Per-path debounce timers, keyed by the affected top-level entry path.</summary>
	private readonly ConcurrentDictionary<string, Timer> _timers = new();

	/// <summary>
	///     Runs the filesystem watch loop until shutdown: installs a recursive
	///     <see cref="FileSystemWatcher" /> on the mapsets path, then waits indefinitely. On shutdown,
	///     disposes any armed-but-not-yet-fired timers and awaits any reconciliation still in flight,
	///     so the DB is never mutated while the host is stopping.
	/// </summary>
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
		watcher.Error += (_, e) => logger.LogWarning(e.GetException(), "Beatmapsets FileSystemWatcher error.");
		watcher.EnableRaisingEvents = true;

		try
		{
			await Task.Delay(Timeout.Infinite, stoppingToken);
		}
		catch (OperationCanceledException)
		{
			// expected on shutdown
		}

		// Disposing an armed-but-not-yet-fired timer is enough to stop it. A timer whose callback
		// already fired is gone from _timers (Settle removes itself), but its reconciliation may
		// still be running; those are tracked separately, so shutdown actually waits for them
		// instead of returning while a beatmap-ingestion pass is still mutating the DB.
		foreach (var timer in _timers.Values) await timer.DisposeAsync();
		await Task.WhenAll(_inFlightSettles.Keys);
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
	///     <see cref="BeatmapIngestionService.DeletedFolderInfix" />, the atomic marker of a folder
	///     mid-deletion) means the folder's *new* name is never a live beatmapset. Debouncing on the
	///     *old* path instead lets <see cref="Settle" />'s own Directory.Exists/File.Exists checks
	///     naturally resolve it to <see cref="BeatmapIngestionService.ReconcileDeletedFolderAsync" />:
	///     the old path no longer exists on disk, and its name (unlike the new one) still carries the
	///     beatmapset's real leading id. Any other rename (e.g., a human renaming a beatmapset folder) still
	///     debounces on the new path as before.
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

	/// <summary>
	///     Arms or re-arms a per-path timer so a burst of filesystem events for the same entry
	///     settles into a single reconciliation after <see cref="DebounceWindow" /> of quiet.
	/// </summary>
	private void Debounce(string? affected)
	{
		if (affected is null) return;

		_timers.AddOrUpdate(affected,
			_ => NewTimer(affected),
			(_, existing) =>
			{
				try
				{
					existing.Change(DebounceWindow, Timeout.InfiniteTimeSpan);
					return existing;
				}
				catch (ObjectDisposedException)
				{
					// Settle already TryRemove'd this entry and is disposing the same Timer
					// concurrently (ConcurrentDictionary.AddOrUpdate's update factory runs outside
					// any lock, so this Change() call and Settle's DisposeAsync() can race). The
					// debounce that fired us is still real, so arm a fresh timer instead of
					// re-arming a dead one.
					return NewTimer(affected);
				}
			});
	}

	/// <summary>
	///     Creates a one-shot timer that runs <see cref="TrackSettle" /> for the given path after
	///     <see cref="DebounceWindow" />.
	/// </summary>
	private Timer NewTimer(string affected)
	{
		return new Timer(_ => TrackSettle(affected), null, DebounceWindow, Timeout.InfiniteTimeSpan);
	}

	/// <summary>
	///     Kicks off <see cref="Settle" /> for the given path and registers the returned task in
	///     <see cref="_inFlightSettles" /> so shutdown can await it.
	/// </summary>
	private void TrackSettle(string affected)
	{
		var task = Settle(affected);
		_inFlightSettles[task] = 0;
		task.ContinueWith(t => _inFlightSettles.TryRemove(t, out _), TaskScheduler.Default);
	}

	/// <summary>
	///     Disposes the path's timer, then reconciles the affected entry: an existing directory
	///     reconciles as a beatmapset folder, an existing ".osz" as a loose archive, and a path that no
	///     longer exists (and is not a ".osz") as a deleted beatmapset folder. A `.deleted_`-marked
	///     folder and any path that fits none of those shapes are skipped.
	/// </summary>
	private async Task Settle(string affected)
	{
		_timers.TryRemove(affected, out var timer);
		if (timer is not null) await timer.DisposeAsync();

		// A `.deleted_` folder is mid-deletion (rename-in-place done, physical removal pending the
		// GC pass), never a live beatmapset, so no reconciliation pathway applies to it.
		if (Path.GetFileName(affected)
		    .Contains(BeatmapIngestionService.DeletedFolderInfix, StringComparison.OrdinalIgnoreCase))
			return;

		try
		{
			var looksLikeOsz = affected.EndsWith(".osz", StringComparison.OrdinalIgnoreCase);
			if (Directory.Exists(affected))
				await ingestion.ReconcileFolderAsync(affected);
			else if (File.Exists(affected) && looksLikeOsz)
				await ingestion.ReconcileOszAsync(affected);
			else if (!File.Exists(affected) && !looksLikeOsz)
				await ingestion.ReconcileDeletedFolderAsync(affected);
			// Else: a .osz that ReconcileOszAsync already extracted then deleted (its own Deleted
			// event lands here too, and must not be treated as a live beatmapset folder disappearing,
			// see BeatmapWatcherServiceTests' repro), or a stray non-.osz file. No pathway applies.
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Failed to reconcile Mapsets path {Path} after a filesystem change.", affected);
		}
	}
}