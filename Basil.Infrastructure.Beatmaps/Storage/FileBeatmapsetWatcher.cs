using System.Threading.Channels;
using Basil.Application.Beatmaps.Persistence;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Beatmaps.Storage;

internal sealed class FileBeatmapsetWatcher : IAsyncEnumerable<BeatmapsetSource>, IDisposable
{
	internal static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(2);

	private readonly Lock _gate = new();
	private readonly ILogger _logger;
	private readonly Dictionary<string, CancellationTokenSource> _pending = new(StringComparer.Ordinal);
	private readonly CancellationTokenRegistration _registration;
	private readonly string _root;
	private readonly Channel<string> _settled = Channel.CreateUnbounded<string>();
	private readonly TimeProvider _time;
	private readonly FileSystemWatcher _watcher;
	private bool _stopped;

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
		_registration = stopping.UnsafeRegister(static state => ((FileBeatmapsetWatcher)state!).Dispose(), this);
	}

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

	public async IAsyncEnumerator<BeatmapsetSource> GetAsyncEnumerator(CancellationToken ct = default)
	{
		try
		{
			await foreach (var name in _settled.Reader.ReadAllAsync(ct))
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
		_registration.Dispose();
		_settled.Writer.TryComplete();
	}

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
			_logger.LogWarning(exception, "Could not report the changed beatmapset entry {FullName}", name);
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