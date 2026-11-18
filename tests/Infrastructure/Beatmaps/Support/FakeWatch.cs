using System.Runtime.CompilerServices;
using Basil.Application.Beatmaps.Persistence;

namespace Basil.Infrastructure.Beatmaps.Tests.Support;

internal sealed class FakeWatch
{
	private readonly Lock _gate = new();
	private readonly List<FakeWatchStream> _streams = [];
	private readonly Dictionary<int, TaskCompletionSource<FakeWatchStream>> _waiting = [];

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

internal sealed class FakeWatchStream : IAsyncEnumerable<BeatmapsetSource>
{
	private readonly TaskCompletionSource _released =
		new(TaskCreationOptions.RunContinuationsAsynchronously);

	public Exception? Failure { get; init; }

	public bool Released => _released.Task.IsCompleted;

	public Task Ending => _released.Task;

	public IAsyncEnumerator<BeatmapsetSource> GetAsyncEnumerator(CancellationToken ct = default)
	{
		return EnumerateAsync(ct).GetAsyncEnumerator(ct);
	}

	private async IAsyncEnumerable<BeatmapsetSource> EnumerateAsync(
		[EnumeratorCancellation] CancellationToken ct)
	{
		try
		{
			await Task.Yield();
			if (Failure is not null) throw Failure;
			await Task.Delay(Timeout.InfiniteTimeSpan, ct);
		}
		finally
		{
			_released.TrySetResult();
		}

		yield break;
	}
}