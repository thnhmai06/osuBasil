using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Basil.Application.Persistence.Beatmaps;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Persistence.Beatmaps;

/// <summary>
///     Watches the beatmapset storage and reports each entry that changed, once its changes have settled; it watches
///     from its creation until the token it was given is cancelled or it is disposed.
/// </summary>
internal sealed class FileBeatmapsetWatcher : IAsyncEnumerable<BeatmapsetSource>, IDisposable
{
	/// <summary>How long an entry must stay unchanged before it is reported.</summary>
	internal static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(2);

	private readonly Lock _gate = new();
	private readonly ILogger _logger;
	private readonly Dictionary<string, CancellationTokenSource> _pending = new(StringComparer.Ordinal);
	private readonly string _root;
	private readonly Channel<string> _settled = Channel.CreateUnbounded<string>();
	private readonly TimeProvider _time;
	private readonly FileSystemWatcher _watcher;
	private bool _stopped;

	/// <summary>Starts watching a storage directory.</summary>
	/// <param name="root">The storage directory's full path.</param>
	/// <param name="time">The clock the settling time is measured on.</param>
	/// <param name="logger">The logger for what could not be reported.</param>
	/// <param name="listEntries">Lists every entry in the storage, to report them all again when watching fails.</param>
	/// <param name="stopping">A token that stops watching.</param>
	internal FileBeatmapsetWatcher(string root, TimeProvider time, ILogger logger,
		Func<IEnumerable<BeatmapsetSource>> listEntries, CancellationToken stopping)
	{
		_root = root;
		_time = time;
		_logger = logger;
		_watcher = new FileSystemWatcher(root)
		{
			IncludeSubdirectories = true,
			NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite |
			               NotifyFilters.Size
		};

		_watcher.Created += (_, args) => Report(TopLevelName(args.FullPath));
		_watcher.Changed += (_, args) => Report(TopLevelName(args.FullPath));
		_watcher.Deleted += (_, args) => Report(TopLevelName(args.FullPath));
		_watcher.Renamed += (_, args) =>
		{
			Report(TopLevelName(args.OldFullPath));
			Report(TopLevelName(args.FullPath));
		};
		_watcher.Error += (_, args) => ReportEveryEntry(args.GetException(), listEntries);
		_watcher.EnableRaisingEvents = true;
	}

	/// <summary>Gets whether watching has stopped: nothing more is reported and the file system is no longer watched.</summary>
	internal bool IsStopped
	{
		get
		{
			lock (_gate)
			{
				return _stopped;
			}
		}
	}

	/// <summary>Stops watching; entries that have not settled are not reported, and reading ends.</summary>
	public void Dispose()
	{
		lock (_gate)
		{
			if (_stopped) return;
			_stopped = true;
			foreach (var debounce in _pending.Values) debounce.Cancel();
			_pending.Clear();
		}

		_watcher.Dispose();
		_settled.Writer.TryComplete();
	}

	/// <summary>Reads the reports; ending the read stops watching.</summary>
	/// <param name="cancellationToken">A token that ends the read.</param>
	/// <returns>Each entry that changed, as it is once its changes have settled.</returns>
	public async IAsyncEnumerator<BeatmapsetSource> GetAsyncEnumerator(CancellationToken cancellationToken = default)
	{
		try
		{
			await foreach (var name in _settled.Reader.ReadAllAsync(cancellationToken))
			{
				var entry = FileBeatmapsetEntryReader.EntryOf(_root, name);
				if (entry is not null) yield return entry;
			}
		}
		finally
		{
			Dispose();
		}
	}

	/// <summary>Notes that a top-level name changed: it is reported once it stays unchanged for the settling time.</summary>
	/// <param name="name">The top-level name; a hidden name or <see langword="null" /> is ignored.</param>
	internal void Report(string? name)
	{
		if (name is null || FileBeatmapsetNames.IsHidden(name)) return;

		lock (_gate)
		{
			if (_stopped) return;
			if (_pending.Remove(name, out var previous)) previous.Cancel();
			_pending.Add(name, new CancellationTokenSource());
		}

		_ = SettleAsync(name);
	}

	private async Task SettleAsync(string name)
	{
		CancellationTokenSource debounce;
		lock (_gate)
		{
			if (!_pending.TryGetValue(name, out debounce!)) return;
		}

		try
		{
			await Task.Delay(SettleTime, _time, debounce.Token);
			lock (_gate)
			{
				if (!_pending.TryGetValue(name, out var current) || !ReferenceEquals(current, debounce)) return;
				_pending.Remove(name);
			}

			_settled.Writer.TryWrite(name);
		}
		catch (OperationCanceledException) when (debounce.IsCancellationRequested)
		{
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Could not report the changed beatmapset entry {Name}", name);
		}
		finally
		{
			debounce.Dispose();
		}
	}

	private void ReportEveryEntry(Exception? exception, Func<IEnumerable<BeatmapsetSource>> listEntries)
	{
		_logger.LogWarning(exception, "Watching {Directory} failed; reporting every entry", _root);
		try
		{
			foreach (var entry in listEntries()) Report(entry.Name);
		}
		catch (Exception list)
		{
			_logger.LogWarning(list, "Could not list the beatmapset entries to report them again");
		}
	}

	private string? TopLevelName(string fullPath)
	{
		var relative = Path.GetRelativePath(_root, fullPath);
		if (string.IsNullOrEmpty(relative) || relative == ".") return null;
		var separator = relative.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
		return separator < 0 ? relative : relative[..separator];
	}
}
