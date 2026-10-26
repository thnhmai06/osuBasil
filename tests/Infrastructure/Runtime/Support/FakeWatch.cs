using System.Runtime.CompilerServices;
using Basil.Application.Persistence.Beatmaps;

namespace Basil.Infrastructure.Runtime.Tests.Support;

/// <summary>
///     Stands in for the storage's watch: hands out one controllable stream per call and records whether each one was
///     released.
/// </summary>
internal sealed class FakeWatch
{
	private readonly Lock _gate = new();
	private readonly List<FakeWatchStream> _streams = [];
	private readonly Dictionary<int, TaskCompletionSource<FakeWatchStream>> _waiting = [];

	/// <summary>Gets how many streams were handed out.</summary>
	public int Count
	{
		get
		{
			lock (_gate)
			{
				return _streams.Count;
			}
		}
	}

	/// <summary>Gets the stream handed out at a position, once it exists.</summary>
	/// <param name="index">The zero-based position of the stream.</param>
	/// <returns>The stream.</returns>
	public FakeWatchStream this[int index]
	{
		get
		{
			lock (_gate)
			{
				return _streams[index];
			}
		}
	}

	/// <summary>Hands out the next stream, which the caller controls through the returned value.</summary>
	/// <param name="failure">The failure the stream throws instead of yielding, or <see langword="null" /> for none.</param>
	/// <returns>The stream handed out.</returns>
	public IAsyncEnumerable<BeatmapsetSource> Next(Exception? failure = null)
	{
		var stream = new FakeWatchStream { Failure = failure };
		List<TaskCompletionSource<FakeWatchStream>> ready;
		lock (_gate)
		{
			_streams.Add(stream);
			ready = [.. _waiting.Values];
			_waiting.Clear();
		}

		foreach (var waiter in ready) waiter.TrySetResult(stream);
		return stream;
	}

	/// <summary>Waits for the stream at a position to be handed out, or reports that it never was.</summary>
	/// <param name="index">The zero-based position of the stream.</param>
	/// <param name="timeout">How long to wait before reporting that it never was.</param>
	/// <returns>The stream, or <see langword="null" /> when it was not handed out in time.</returns>
	public async Task<FakeWatchStream?> WaitAsync(int index, TimeSpan timeout)
	{
		TaskCompletionSource<FakeWatchStream> waiter;
		lock (_gate)
		{
			if (_streams.Count > index) return _streams[index];
			waiter = new TaskCompletionSource<FakeWatchStream>(TaskCreationOptions.RunContinuationsAsynchronously);
			_waiting[index] = waiter;
		}

		var ready = await Task.WhenAny(waiter.Task, Task.Delay(timeout));
		return ready == waiter.Task ? waiter.Task.Result : null;
	}
}

/// <summary>A stream the storage hands out from its watch, which ends when it fails or when its watch stops.</summary>
internal sealed class FakeWatchStream : IAsyncEnumerable<BeatmapsetSource>
{
	private readonly TaskCompletionSource _released =
		new(TaskCreationOptions.RunContinuationsAsynchronously);

	/// <summary>Gets the failure the stream throws instead of yielding, or <see langword="null" /> for none.</summary>
	public Exception? Failure { get; init; }

	/// <summary>Gets whether the stream's watch was released.</summary>
	public bool Released => _released.Task.IsCompleted;

	/// <summary>Gets a task that completes when the stream's watch is released.</summary>
	public Task Ending => _released.Task;

	/// <inheritdoc />
	public IAsyncEnumerator<BeatmapsetSource> GetAsyncEnumerator(CancellationToken cancellationToken = default)
	{
		return EnumerateAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
	}

	private async IAsyncEnumerable<BeatmapsetSource> EnumerateAsync(
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		try
		{
			await Task.Yield();
			if (Failure is not null) throw Failure;
			await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
		}
		finally
		{
			_released.TrySetResult();
		}

		yield break;
	}
}