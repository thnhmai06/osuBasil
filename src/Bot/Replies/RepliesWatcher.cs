using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Bot.Replies;

/// <summary>
///     Reads the replies file at startup and again whenever it is saved, so the wording changes while the server
///     runs.
/// </summary>
internal sealed class RepliesWatcher(ReplyCatalog catalog, TimeProvider time, ILogger<RepliesWatcher> logger)
	: IHostedService, IDisposable
{
	private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(500);

	private readonly Lock _gate = new();
	private ITimer? _pending;
	private bool _stopped;
	private FileSystemWatcher? _watcher;

	/// <summary>
	///     Gets a value that indicates whether saves of the file are noticed by the file system; tests turn it off and
	///     signal changes themselves.
	/// </summary>
	internal bool WatchFileSystem { get; init; } = true;

	/// <inheritdoc />
	public void Dispose()
	{
		lock (_gate)
		{
			_stopped = true;
			_pending?.Dispose();
			_pending = null;
		}

		_watcher?.Dispose();
	}

	/// <inheritdoc />
	public Task StartAsync(CancellationToken cancellationToken)
	{
		catalog.Load();

		if (!WatchFileSystem) return Task.CompletedTask;

		var directory = Path.GetDirectoryName(catalog.Path)!;
		try
		{
			_watcher = new FileSystemWatcher(directory, Path.GetFileName(catalog.Path))
			{
				NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
			};
			_watcher.Changed += (_, _) => Changed();
			_watcher.Created += (_, _) => Changed();
			_watcher.Renamed += (_, _) => Changed();
			_watcher.EnableRaisingEvents = true;
		}
		catch (Exception exception) when
			(exception is IOException or ArgumentException or PlatformNotSupportedException)
		{
			logger.LogWarning(exception,
				"The replies file {Path} is not watched; the wording changes at the next start",
				catalog.Path);
		}

		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task StopAsync(CancellationToken cancellationToken)
	{
		Dispose();
		return Task.CompletedTask;
	}

	/// <summary>Reads the file again once it has stayed unchanged for a moment, as an editor saves it in several steps.</summary>
	internal void Changed()
	{
		lock (_gate)
		{
			if (_stopped) return;

			_pending?.Dispose();
			_pending = time.CreateTimer(_ => catalog.Reload(), null, Settle, Timeout.InfiniteTimeSpan);
		}
	}
}