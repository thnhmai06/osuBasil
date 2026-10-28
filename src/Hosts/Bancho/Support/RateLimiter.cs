using System.Collections.Concurrent;

namespace Basil.Host.Bancho.Support;

/// <summary>Counts events per key inside a sliding window.</summary>
internal sealed class RateLimiter(TimeProvider clock)
{
	private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _events = new();

	/// <summary>Records an event unless the key already reached its limit in the window.</summary>
	/// <param name="key">What is limited.</param>
	/// <param name="count">How many events the window allows.</param>
	/// <param name="window">The length of the window.</param>
	/// <returns><see langword="true" /> when the event is allowed and recorded.</returns>
	public bool TryAcquire(string key, int count, TimeSpan window)
	{
		var queue = _events.GetOrAdd(key, _ => new Queue<DateTimeOffset>());
		lock (queue)
		{
			Trim(queue, window);
			if (queue.Count >= count) return false;
			queue.Enqueue(clock.GetUtcNow());
			return true;
		}
	}

	/// <summary>Records an event without checking a limit.</summary>
	/// <param name="key">What is counted.</param>
	public void Record(string key)
	{
		var queue = _events.GetOrAdd(key, _ => new Queue<DateTimeOffset>());
		lock (queue)
		{
			queue.Enqueue(clock.GetUtcNow());
		}
	}

	/// <summary>Counts the events of a key inside the window.</summary>
	/// <param name="key">What is counted.</param>
	/// <param name="window">The length of the window.</param>
	/// <returns>The number of events recorded in the window.</returns>
	public int Count(string key, TimeSpan window)
	{
		if (!_events.TryGetValue(key, out var queue)) return 0;
		lock (queue)
		{
			Trim(queue, window);
			return queue.Count;
		}
	}

	private void Trim(Queue<DateTimeOffset> queue, TimeSpan window)
	{
		var oldest = clock.GetUtcNow() - window;
		while (queue.Count > 0 && queue.Peek() <= oldest) queue.Dequeue();
	}
}
