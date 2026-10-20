using Basil.Application.Objects.Beatmaps;
using Basil.Application.Storage.Beatmaps;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Runtime.Beatmaps;

/// <summary>
///     Keeps the server's beatmapsets in step with the beatmapset storage: a full scan that finishes before the
///     server serves clients, then every entry that changes.
/// </summary>
internal sealed class BeatmapsetStorageWatcher(
	IBeatmapsetService beatmapsets,
	IBeatmapsetStorage storage,
	ILogger<BeatmapsetStorageWatcher> logger) : BackgroundService
{
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
			var result = await beatmapsets.ScanAsync(cancellationToken).ConfigureAwait(false);
			logger.LogInformation(
				"Scanned the beatmapset storage: {Imported} taken in, {Updated} updated, {Forgotten} forgotten",
				result.Imported, result.Updated, result.Forgotten);
		}
		catch (Exception exception) when (exception is not OperationCanceledException ||
		                                  !cancellationToken.IsCancellationRequested)
		{
			logger.LogError(exception, "Could not scan the beatmapset storage");
		}

		await base.StartAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Stops watching the storage, then lets the remaining hosted services stop.</summary>
	/// <param name="cancellationToken">A token that cancels stopping.</param>
	public override async Task StopAsync(CancellationToken cancellationToken)
	{
		_stopping.Cancel();
		await base.StopAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Releases the watch's cancellation.</summary>
	public override void Dispose()
	{
		_stopping.Dispose();
		base.Dispose();
	}

	/// <inheritdoc />
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		try
		{
			await foreach (var entry in _changes!.ConfigureAwait(false))
				try
				{
					var result = await beatmapsets.ScanAsync(entry, stoppingToken).ConfigureAwait(false);
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
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested ||
		                                         _stopping.IsCancellationRequested)
		{
		}
	}
}