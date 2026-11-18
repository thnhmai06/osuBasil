using Basil.Application.Beatmaps.Objects;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Beatmaps.Workers;

internal sealed class BeatmapsetStorageWatcher(
	Beatmapsets beatmapsets,
	IBeatmapsetStorage storage,
	TimeProvider time,
	ILogger<BeatmapsetStorageWatcher> logger) : BackgroundService
{
	private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(5);

	private readonly CancellationTokenSource _stopping = new();

	private IAsyncEnumerable<BeatmapsetSource>? _changes;

	public override async Task StartAsync(CancellationToken ct)
	{
		_changes = storage.WatchAsync(_stopping.Token);
		try
		{
			var result = await beatmapsets.ScanAsync(ct);
			logger.LogInformation(
				"Scanned the beatmapset storage: {Imported} taken in, {Updated} updated, {Forgotten} forgotten",
				result.Imported, result.Updated, result.Forgotten);
		}
		catch (Exception exception) when (exception is not OperationCanceledException ||
		                                  !ct.IsCancellationRequested)
		{
			logger.LogError(exception, "Could not scan the beatmapset storage");
		}

		await base.StartAsync(ct);
	}

	public override async Task StopAsync(CancellationToken ct)
	{
		await _stopping.CancelAsync();
		await base.StopAsync(ct);

		if (Interlocked.Exchange(ref _changes, null) is not { } changes) return;

		try
		{
			await foreach (var _ in changes.WithCancellation(_stopping.Token))
			{
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	public override void Dispose()
	{
		_stopping.Dispose();
		base.Dispose();
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		// The watch belongs to this method once it runs; a start-up that ended before this leaves it to StopAsync.
		if (Interlocked.Exchange(ref _changes, null) is not { } changes) return;

		while (true)
		{
			try
			{
				await foreach (var entry in changes.WithCancellation(stoppingToken))
					await ScanEntryAsync(entry, stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested ||
			                                         _stopping.IsCancellationRequested)
			{
				return;
			}
			catch (Exception exception)
			{
				logger.LogError(exception, "The beatmapset storage watch stopped; watching the storage again");
			}

			if (!await WaitToRestartAsync(stoppingToken)) return;

			changes = storage.WatchAsync(stoppingToken);
		}
	}

	private async Task ScanEntryAsync(BeatmapsetSource entry, CancellationToken stoppingToken)
	{
		try
		{
			var result = await beatmapsets.ScanAsync(entry, stoppingToken);
			if (result is not { Imported: 0, Updated: 0, Forgotten: 0 })
				logger.LogInformation(
					"Beatmapset entry {FullName}: {Imported} taken in, {Updated} updated, {Forgotten} forgotten",
					entry.Name, result.Imported, result.Updated, result.Forgotten);
		}
		catch (Exception exception) when (exception is not OperationCanceledException ||
		                                  !stoppingToken.IsCancellationRequested)
		{
			logger.LogError(exception, "Could not scan beatmapset entry {FullName}", entry.Name);
		}
	}

	private async Task<bool> WaitToRestartAsync(CancellationToken stoppingToken)
	{
		try
		{
			await Task.Delay(RestartDelay, time, stoppingToken);
			return true;
		}
		catch (OperationCanceledException)
		{
			return false;
		}
	}
}