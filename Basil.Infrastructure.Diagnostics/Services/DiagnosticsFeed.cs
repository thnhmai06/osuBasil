using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Basil.Application.Diagnostics.Services;
using Basil.Shared.Structures.Data;

namespace Basil.Infrastructure.Diagnostics.Services;

internal sealed class DiagnosticsFeed : IDiagnosticsFeed
{
	private readonly ConcurrentSet<Watcher> _watchers = [];

	internal int Watchers => _watchers.Count;

	public IAsyncEnumerable<DiagnosticsSample> Watch(string? category = null,
		CancellationToken ct = default)
	{
		category = category?.ToLowerInvariant();
		if (category is not null && !DiagnosticsSample.Categories.Contains(category))
			throw new KeyNotFoundException($"No such diagnostics category: {category}.");

		// The watcher joins now, not when the first sample is read, so no sample taken in between is missed.
		var watcher = new Watcher(category);
		_watchers.Add(watcher);

		return Read(watcher, ct);
	}

	internal void Publish(DiagnosticsSample sample)
	{
		foreach (var watcher in _watchers)
			watcher.Queue.Writer.TryWrite(watcher.Category is null ? sample : sample.Only(watcher.Category));
	}

	private async IAsyncEnumerable<DiagnosticsSample> Read(Watcher watcher,
		[EnumeratorCancellation] CancellationToken ct)
	{
		try
		{
			await foreach (var sample in watcher.Queue.Reader.ReadAllAsync(ct)) yield return sample;
		}
		finally
		{
			_watchers.Remove(watcher);
		}
	}

	private sealed class Watcher(string? category)
	{
		public string? Category { get; } = category;

		public Channel<DiagnosticsSample> Queue { get; } = Channel.CreateBounded<DiagnosticsSample>(
			new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
	}
}