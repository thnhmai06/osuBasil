using Basil.Application.Services.Contracts.Beatmaps;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Runtime.Beatmaps;

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
		Directory.CreateDirectory(directory);

		using var watcher = new FileSystemWatcher(directory, "*.osz")
		{
			IncludeSubdirectories = false,
			NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
		};
		watcher.Created += OnChanged;
		watcher.Changed += OnChanged;
		watcher.Renamed += OnRenamed;
		watcher.EnableRaisingEvents = true;

		try
		{
			await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
		}
		finally
		{
			lock (_gate)
			{
				foreach (var pending in _pending.Values) pending.Cancel();
				_pending.Clear();
			}
		}

		void OnChanged(object sender, FileSystemEventArgs args) => Schedule(args.FullPath, stoppingToken);
		void OnRenamed(object sender, RenamedEventArgs args) => Schedule(args.FullPath, stoppingToken);
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

	private async Task ImportAfterDebounceAsync(string path, CancellationTokenSource debounce, CancellationToken stoppingToken)
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
		catch (OperationCanceledException) when (debounce.IsCancellationRequested || stoppingToken.IsCancellationRequested)
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
