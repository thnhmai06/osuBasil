using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Basil.Application.Common.Diagnostics;
using Basil.Application.Diagnostics.Services;

namespace Basil.Infrastructure.Diagnostics.Services;

/// <summary>
///     Gives every watcher its own queue of one sample that drops the older one, so a slow watcher sees the newest
///     sample and never delays the others.
/// </summary>
internal sealed class DiagnosticsFeed : IDiagnosticsFeed
{
	private readonly Lock _sync = new();
	private readonly List<Watcher> _watchers = [];

	/// <summary>Gets how many watchers are following the feed.</summary>
	internal int Watchers
	{
		get
		{
			lock (_sync)
			{
				return _watchers.Count;
			}
		}
	}

	/// <inheritdoc />
	public IAsyncEnumerable<DiagnosticsSample> Watch(string? category = null,
		CancellationToken ct = default)
	{
		category = category?.ToLowerInvariant();
		if (category is not null && !DiagnosticsSample.Categories.Contains(category))
			throw new KeyNotFoundException($"No such diagnostics category: {category}.");

		// The watcher joins now, not when the first sample is read, so no sample taken in between is missed.
		var watcher = new Watcher(category);
		lock (_sync)
		{
			_watchers.Add(watcher);
		}

		return Read(watcher, ct);
	}

	/// <summary>Gives a sample to every watcher, replacing the one it has not read yet.</summary>
	/// <param name="sample">The sample.</param>
	internal void Publish(DiagnosticsSample sample)
	{
		Watcher[] watchers;
		lock (_sync)
		{
			watchers = [.. _watchers];
		}

		foreach (var watcher in watchers)
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
			lock (_sync)
			{
				_watchers.Remove(watcher);
			}
		}
	}

	private sealed class Watcher(string? category)
	{
		public string? Category { get; } = category;

		public Channel<DiagnosticsSample> Queue { get; } = Channel.CreateBounded<DiagnosticsSample>(
			new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
	}
}