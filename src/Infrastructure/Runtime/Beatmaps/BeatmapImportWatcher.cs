using Basil.Application.Services.Contracts.Beatmaps;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Runtime.Beatmaps;

/// <summary>Imports the beatmapset archives placed in the import directory: those waiting at startup and every one added later; at startup it also forgets beatmapsets whose archive is gone.</summary>
internal sealed class BeatmapImportWatcher(
	IBeatmapsetService beatmapsets,
	IOptions<BeatmapImportOptions> options,
	TimeProvider time,
	ILogger<BeatmapImportWatcher> logger) : BackgroundService
{
	private readonly object _gate = new();
	private readonly Dictionary<string, CancellationTokenSource> _pending = new(StringComparer.OrdinalIgnoreCase);

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var directory = options.Value.Directory;
		FileSystemWatcher? watcher = null;
		try
		{
			Directory.CreateDirectory(directory);
			watcher = new FileSystemWatcher(directory, "*.osz")
			{
				IncludeSubdirectories = false,
				NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
			};
			watcher.Created += OnChanged;
			watcher.Changed += OnChanged;
			watcher.Renamed += OnRenamed;
			watcher.Error += OnError;
			watcher.EnableRaisingEvents = true;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
		{
			watcher?.Dispose();
			logger.LogError(exception,
				"Beatmap archives in {Directory} will not be imported: the directory cannot be watched.", directory);
			return;
		}

		try
		{
			var forgotten = await beatmapsets.ScanAsync(stoppingToken);
			logger.LogInformation("Forgot {BeatmapsetCount} beatmapsets whose archive is gone.", forgotten);
		}
		catch (Exception exception) when (exception is not OperationCanceledException ||
		                                  !stoppingToken.IsCancellationRequested)
		{
			logger.LogError(exception, "Could not check the stored beatmapsets.");
		}

		try
		{
			var imported = 0;
			foreach (var path in Directory.EnumerateFiles(directory, "*.osz", SearchOption.TopDirectoryOnly))
			{
				var status = await BeatmapArchiveImporter.ImportAsync(path, beatmapsets, logger, stoppingToken);
				if (status == BeatmapArchiveImporter.ImportStatus.Locked) Schedule(path, stoppingToken);
				else if (status == BeatmapArchiveImporter.ImportStatus.Imported) imported++;
			}

			logger.LogInformation("Imported {ArchiveCount} waiting beatmap archives.", imported);
		}
		catch (Exception exception) when (exception is not OperationCanceledException ||
		                                  !stoppingToken.IsCancellationRequested)
		{
			logger.LogError(exception, "Could not import the waiting beatmap archives.");
		}

		try
		{
			await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
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

		void OnChanged(object sender, FileSystemEventArgs args) => Schedule(args.FullPath, stoppingToken);
		void OnRenamed(object sender, RenamedEventArgs args) => Schedule(args.FullPath, stoppingToken);

		void OnError(object sender, ErrorEventArgs args)
		{
			logger.LogWarning(args.GetException(), "Watching {Directory} failed; checking every waiting archive again.",
				directory);
			try
			{
				foreach (var path in Directory.EnumerateFiles(directory, "*.osz", SearchOption.TopDirectoryOnly))
					Schedule(path, stoppingToken);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				logger.LogWarning(exception, "Could not list the waiting beatmap archives in {Directory}.", directory);
			}
		}
	}

	private void Schedule(string path, CancellationToken stoppingToken)
	{
		if (stoppingToken.IsCancellationRequested) return;

		var debounce = new CancellationTokenSource();
		lock (_gate)
		{
			if (_pending.Remove(path, out var previous)) previous.Cancel();
			_pending.Add(path, debounce);
		}

		_ = ImportAfterDebounceAsync(path, debounce, stoppingToken);
	}

	private async Task ImportAfterDebounceAsync(string path, CancellationTokenSource debounce,
		CancellationToken stoppingToken)
	{
		try
		{
			await Task.Delay(TimeSpan.FromSeconds(2), time, debounce.Token);
			lock (_gate)
			{
				if (!_pending.TryGetValue(path, out var current) || !ReferenceEquals(current, debounce)) return;
				_pending.Remove(path);
			}

			var status = await BeatmapArchiveImporter.ImportAsync(path, beatmapsets, logger, stoppingToken);
			if (status == BeatmapArchiveImporter.ImportStatus.Locked) Schedule(path, stoppingToken);
		}
		catch (OperationCanceledException) when (debounce.IsCancellationRequested ||
		                                         stoppingToken.IsCancellationRequested)
		{
		}
		catch (Exception exception)
		{
			logger.LogError(exception, "Could not process beatmap archive {Path}.", path);
		}
		finally
		{
			debounce.Dispose();
		}
	}
}