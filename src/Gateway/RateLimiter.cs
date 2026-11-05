using System.Collections.Concurrent;

namespace Basil.Gateway;

/// <summary>Counts events per key inside a sliding window; keys whose events all expired are forgotten.</summary>
public sealed class RateLimiter(TimeProvider clock)
{
	/// <summary>
	///     The most keys remembered at the same time; beyond it the keys with the fewest recent events are forgotten
	///     first.
	/// </summary>
	public const int MaxKeys = 100_000;

	private static readonly TimeSpan PurgeEvery = TimeSpan.FromMinutes(1);
	private static readonly TimeSpan DefaultWindow = TimeSpan.FromHours(1);

	private readonly ConcurrentDictionary<string, Entry> _events = new();
	private long _nextPurge;

	/// <summary>Gets how many keys are remembered.</summary>
	public int KeyCount => _events.Count;

	/// <summary>Records an event unless the key already reached its limit in the window.</summary>
	/// <param name="key">What is limited.</param>
	/// <param name="count">How many events the window allows.</param>
	/// <param name="window">The length of the window.</param>
	/// <returns><see langword="true" /> when the event is allowed and recorded.</returns>
	public bool TryAcquire(string key, int count, TimeSpan window)
	{
		var entry = Get(key);
		var queue = entry.Events;
		lock (entry)
		{
			entry.Window = window > entry.Window ? window : entry.Window;
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
		var entry = Get(key);
		lock (entry)
		{
			entry.Events.Enqueue(clock.GetUtcNow());
		}
	}

	/// <summary>Takes back the latest event of a key, such as an attempt that turned out to be a success.</summary>
	/// <param name="key">What is counted.</param>
	public void Refund(string key)
	{
		if (!_events.TryGetValue(key, out var entry)) return;
		var queue = entry.Events;
		lock (entry)
		{
			var kept = queue.Take(Math.Max(queue.Count - 1, 0)).ToArray();
			queue.Clear();
			foreach (var moment in kept) queue.Enqueue(moment);
		}
	}

	/// <summary>Counts the events of a key inside the window.</summary>
	/// <param name="key">What is counted.</param>
	/// <param name="window">The length of the window.</param>
	/// <returns>The number of events recorded in the window.</returns>
	public int Count(string key, TimeSpan window)
	{
		if (!_events.TryGetValue(key, out var entry)) return 0;
		var queue = entry.Events;
		lock (entry)
		{
			entry.Window = window > entry.Window ? window : entry.Window;
			Trim(queue, window);
			return queue.Count;
		}
	}

	private void Trim(Queue<DateTimeOffset> queue, TimeSpan window)
	{
		var oldest = clock.GetUtcNow() - window;
		while (queue.Count > 0 && queue.Peek() <= oldest) queue.Dequeue();
	}

	private Entry Get(string key)
	{
		Purge();
		return _events.GetOrAdd(key, _ => new Entry());
	}

	// Runs at most once a minute, or at once when the table is over its cap.
	private void Purge()
	{
		var now = clock.GetTimestamp();
		var due = Interlocked.Read(ref _nextPurge);
		if (_events.Count <= MaxKeys && now < due) return;
		if (Interlocked.CompareExchange(ref _nextPurge,
			    now + (long)(PurgeEvery.TotalSeconds * clock.TimestampFrequency),
			    due) != due) return;

		foreach (var (key, entry) in _events)
		{
			lock (entry)
			{
				Trim(entry.Events, entry.Window == TimeSpan.Zero ? DefaultWindow : entry.Window);
				if (entry.Events.Count > 0) continue;
			}

			_events.TryRemove(key, out _);
		}

		// Beyond the cap the keys with the fewest recent events go first (down to 90% of the cap, so the next flood
		// request does not purge again): a key that holds a lockout has the most events and is the last to be forgotten.
		if (_events.Count <= MaxKeys) return;
		var weakest = _events
			.Select(pair =>
			{
				lock (pair.Value)
				{
					return (pair.Key, Weight: pair.Value.Events.Count);
				}
			})
			.OrderBy(item => item.Weight)
			.Take(_events.Count - MaxKeys / 10 * 9)
			.ToList();
		foreach (var (key, _) in weakest) _events.TryRemove(key, out _);
	}

	private sealed class Entry
	{
		public readonly Queue<DateTimeOffset> Events = new();
		public TimeSpan Window;
	}
}