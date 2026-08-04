using Basil.Application.Abstractions.Content;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Content;

/// <inheritdoc cref="IMotdProvider" />
/// <remarks>
///     Reads <c>MOTD.txt</c> from the given data directory once at construction and caches the
///     result; a <see cref="FileSystemWatcher" /> on that single file keeps the cache fresh so a
///     login never re-reads the file from disk. Debounced by a short fixed delay to avoid reading a
///     half-written file mid-save — simpler than <c>BeatmapWatcherService</c>'s per-path timer
///     dictionary since only one file is ever watched here.
/// </remarks>
public sealed class FileMotdProvider : IMotdProvider, IDisposable
{
	private const string FileName = "MOTD.txt";
	private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(250);

	private readonly string _filePath;
	private readonly ILogger<FileMotdProvider> _logger;
	private readonly Lock _timerLock = new();
	private readonly FileSystemWatcher _watcher;
	private volatile string? _cachedText;
	private Timer? _debounceTimer;

	/// <summary>
	///     Creates the provider, performing the initial read and arming the watcher.
	/// </summary>
	/// <param name="dataDirectory">The directory <c>MOTD.txt</c> lives in, created if missing.</param>
	/// <param name="logger">The logger used to report watcher errors.</param>
	public FileMotdProvider(string dataDirectory, ILogger<FileMotdProvider> logger)
	{
		_logger = logger;
		_filePath = Path.Combine(dataDirectory, FileName);

		Directory.CreateDirectory(dataDirectory);
		_cachedText = ReadFile();

		_watcher = new FileSystemWatcher(dataDirectory, FileName)
		{
			NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
		};
		_watcher.Created += (_, _) => Debounce();
		_watcher.Changed += (_, _) => Debounce();
		_watcher.Deleted += (_, _) => Debounce();
		_watcher.Renamed += (_, _) => Debounce();
		_watcher.Error += (_, e) => _logger.LogWarning(e.GetException(), "MOTD FileSystemWatcher error.");
		_watcher.EnableRaisingEvents = true;
	}

	/// <inheritdoc />
	public string? GetText()
	{
		return _cachedText;
	}

	/// <inheritdoc />
	public void Dispose()
	{
		_watcher.Dispose();
		lock (_timerLock) _debounceTimer?.Dispose();
	}

	private void Debounce()
	{
		lock (_timerLock)
		{
			_debounceTimer?.Dispose();
			_debounceTimer = new Timer(_ => _cachedText = ReadFile(), null, DebounceWindow, Timeout.InfiniteTimeSpan);
		}
	}

	private string? ReadFile()
	{
		if (!File.Exists(_filePath)) return null;

		try
		{
			var text = File.ReadAllText(_filePath).TrimEnd();
			return string.IsNullOrEmpty(text) ? null : text;
		}
		catch (IOException)
		{
			// File is mid-write (locked); keep the stale cached value, the next debounced event retries.
			return _cachedText;
		}
	}
}
