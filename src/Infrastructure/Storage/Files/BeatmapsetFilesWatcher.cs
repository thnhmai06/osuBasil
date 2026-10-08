using System.Globalization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Storage.Files;

/// <summary>Keeps the .osz archives of stored beatmapsets in step with their files when the files change on disk.</summary>
internal sealed class BeatmapsetFilesWatcher(
	DataPaths paths,
	FileBeatmapsetStorage storage,
	TimeProvider time,
	ILogger<BeatmapsetFilesWatcher> logger) : BackgroundService
{
	private readonly object _gate = new();
	private readonly Dictionary<int, CancellationTokenSource> _pending = [];

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		FileSystemWatcher? watcher = null;
		try
		{
			watcher = new FileSystemWatcher(paths.Beatmaps)
			{
				IncludeSubdirectories = true,
				NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
			};
			watcher.Created += OnChanged;
			watcher.Changed += OnChanged;
			watcher.Deleted += OnChanged;
			watcher.Renamed += OnRenamed;
			watcher.Error += OnError;
			watcher.EnableRaisingEvents = true;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
		{
			watcher?.Dispose();
			logger.LogError(exception, "Beatmapset files in {Directory} will not be watched.", paths.Beatmaps);
			return;
		}

		try
		{
			foreach (var id in storage.StoredSetIds())
			{
				try
				{
					await storage.SyncArchivesAsync(id, stoppingToken);
				}
				catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
				{
					break;
				}
				catch (Exception exception)
				{
					logger.LogError(exception, "Could not synchronize the archives of beatmapset {SetId}.", id);
				}
			}

			try
			{
				await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
			}
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
		{
			logger.LogError(exception, "Could not list stored beatmapsets while synchronizing their archives.");
		}
		finally
		{
			watcher.Dispose();
			lock (_gate)
			{
				foreach (var pending in _pending.Values) pending.Cancel();
				_pending.Clear();
			}
		}

		void OnChanged(object sender, FileSystemEventArgs args) => Schedule(SetIdOf(args.FullPath), stoppingToken);
		void OnRenamed(object sender, RenamedEventArgs args) => Schedule(SetIdOf(args.FullPath), stoppingToken);

		void OnError(object sender, ErrorEventArgs args)
		{
			logger.LogWarning(args.GetException(), "Watching {Directory} failed; synchronizing every stored set.", paths.Beatmaps);
			try
			{
				foreach (var id in storage.StoredSetIds()) Schedule(id, stoppingToken);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
			{
				logger.LogWarning(exception, "Could not list stored beatmapsets to synchronize their archives.");
			}
		}
	}

	private int? SetIdOf(string path)
	{
		var relative = Path.GetRelativePath(paths.Beatmaps, path);
		var segment = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
			StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
		return segment is { Length: > 0 } && segment.All(char.IsAsciiDigit) &&
		       int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
			? id
			: null;
	}

	private void Schedule(int? id, CancellationToken stoppingToken)
	{
		if (id is null || stoppingToken.IsCancellationRequested) return;

		var debounce = new CancellationTokenSource();
		lock (_gate)
		{
			if (_pending.Remove(id.Value, out var previous)) previous.Cancel();
			_pending.Add(id.Value, debounce);
		}
		_ = SyncAfterDebounceAsync(id.Value, debounce, stoppingToken);
	}

	private async Task SyncAfterDebounceAsync(int setId, CancellationTokenSource debounce, CancellationToken stoppingToken)
	{
		try
		{
			await Task.Delay(TimeSpan.FromSeconds(2), time, debounce.Token);
			lock (_gate)
			{
				if (!_pending.TryGetValue(setId, out var current) || !ReferenceEquals(current, debounce)) return;
				_pending.Remove(setId);
			}

			await storage.SyncArchivesAsync(setId, stoppingToken);
		}
		catch (OperationCanceledException) when (debounce.IsCancellationRequested || stoppingToken.IsCancellationRequested)
		{
		}
		catch (Exception exception)
		{
			logger.LogError(exception, "Could not synchronize the archives of beatmapset {SetId}.", setId);
		}
		finally
		{
			debounce.Dispose();
		}
	}
}
