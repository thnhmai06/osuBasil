using Basil.Application.Beatmaps.Objects;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Beatmaps.Workers;

/// <summary>
///     Keeps the server's beatmapsets in step with the beatmapset storage: a full scan that finishes before the
///     server serves clients, then every entry that changes.
/// </summary>
internal sealed class BeatmapsetStorageWatcher(
	Beatmapsets beatmapsets,
	IBeatmapsetStorage storage,
	TimeProvider time,
	ILogger<BeatmapsetStorageWatcher> logger) : BackgroundService
{
	/// <summary>How long to wait before watching the storage again after its watch stops.</summary>
	private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(5);

	private readonly CancellationTokenSource _stopping = new();

	/// <summary>The watched entries to scan once the full scan is done.</summary>
	private IAsyncEnumerable<BeatmapsetSource>? _changes;

	/// <summary>Starts watching the storage, scans it fully, and only then starts serving the changed entries.</summary>
	/// <param name="cancellationToken">A token that cancels starting.</param>
	/// <remarks>
	///     Watching begins before the scan, so no change made during the scan is lost. The scan finishes here, before
	///     the server serves clients; a scan that fails for any reason other than cancellation is logged and the server
	///     starts anyway.
	/// </remarks>
	public override async Task StartAsync(CancellationToken cancellationToken)
	{
		_changes = storage.WatchAsync(_stopping.Token);
		try
		{
			var result = await beatmapsets.ScanAsync(cancellationToken);
			logger.LogInformation(
				"Scanned the beatmapset storage: {Imported} taken in, {Updated} updated, {Forgotten} forgotten",
				result.Imported, result.Updated, result.Forgotten);
		}
		catch (Exception exception) when (exception is not OperationCanceledException ||
		                                  !cancellationToken.IsCancellationRequested)
		{
			logger.LogError(exception, "Could not scan the beatmapset storage");
		}

		await base.StartAsync(cancellationToken);
	}

	/// <summary>Stops watching the storage, then lets the remaining hosted services stop.</summary>
	/// <param name="cancellationToken">A token that cancels stopping.</param>
	/// <remarks>
	///     A start-up that was cancelled ends before the watch is read; stopping reads it here instead, so the storage
	///     is not left watched.
	/// </remarks>
	public override async Task StopAsync(CancellationToken cancellationToken)
	{
		await _stopping.CancelAsync();
		await base.StopAsync(cancellationToken);

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

	/// <summary>Releases the watch's cancellation.</summary>
	public override void Dispose()
	{
		_stopping.Dispose();
		base.Dispose();
	}

	/// <inheritdoc />
	/// <remarks>
	///     A watch that stops for any reason other than stopping the service is logged, and the storage is watched again
	///     after a short delay, so one broken entry or one broken storage call does not leave the server with stale
	///     beatmapsets.
	/// </remarks>
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
					"Beatmapset entry {Name}: {Imported} taken in, {Updated} updated, {Forgotten} forgotten",
					entry.Name, result.Imported, result.Updated, result.Forgotten);
		}
		catch (Exception exception) when (exception is not OperationCanceledException ||
		                                  !stoppingToken.IsCancellationRequested)
		{
			logger.LogError(exception, "Could not scan beatmapset entry {Name}", entry.Name);
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