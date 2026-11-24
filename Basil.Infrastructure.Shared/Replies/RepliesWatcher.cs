using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Shared.Replies;

/// <summary>Loads the replies file of a surface at start-up and reloads it when the operator edits it.</summary>
internal sealed class RepliesWatcher<TReply>(
	ReplyCatalog<TReply> catalog,
	TimeProvider time,
	ILogger<RepliesWatcher<TReply>> logger) : IHostedService, IDisposable where TReply : struct, Enum
{
	private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(500);

	private readonly Lock _gate = new();
	private ITimer? _pending;
	private bool _stopped;
	private FileSystemWatcher? _watcher;

	internal bool WatchFileSystem { get; init; } = true;

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

	public Task StartAsync(CancellationToken ct)
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

	public Task StopAsync(CancellationToken ct)
	{
		Dispose();
		return Task.CompletedTask;
	}

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