using System.Collections.Concurrent;

namespace Basil.Application.Shared.Waiting;

/// <summary>What <see cref="Pending{TKey,TResult}.Complete"/> did with a result.</summary>
public enum PendingOutcome : byte
{
	/// <summary>Nothing waits under the key.</summary>
	Unknown,

	/// <summary>The waiter was handed the result.</summary>
	Delivered,

	/// <summary>The waiter gave up and its row is not stored yet; the result is kept for <see cref="Pending{TKey,TResult}.Store"/>.</summary>
	Held,

	/// <summary>The waiter gave up and its row is stored; the caller writes the result to the row.</summary>
	Stored
}

/// <summary>
/// Holds a request that has been sent until its result arrives or the caller gives up, and keeps a late result for
/// the row the caller then stores. Callers wait outside any notification handler; handlers only call
/// <see cref="Complete"/>.
/// </summary>
public sealed class Pending<TKey, TResult>(TimeProvider clock) where TKey : notnull
{
	private readonly ConcurrentDictionary<TKey, Entry> _entries = new();

	/// <summary>Registers a waiter. Call it before the request is sent; false when the key is already pending.</summary>
	public bool Register(TKey key)
	{
		return _entries.TryAdd(key, new Entry());
	}

	/// <summary>Registers a key whose row already exists, so a result arriving later is written to it directly.</summary>
	public bool RegisterStored(TKey key, int id)
	{
		return _entries.TryAdd(key, new Entry { State = State.Stored, Id = id });
	}

	/// <summary>Waits for the result of a registered key. On timeout the entry stays, waiting for <see cref="Store"/>.</summary>
	/// <returns>Whether the result arrived, and the result.</returns>
	public async Task<(bool Arrived, TResult? Result)> WaitAsync(TKey key, TimeSpan limit, CancellationToken ct)
	{
		if (!_entries.TryGetValue(key, out var entry)) return (false, default);

		try
		{
			return (true, await entry.Source.Task.WaitAsync(limit, clock, ct));
		}
		catch (TimeoutException)
		{
			lock (entry.Gate)
			{
				if (entry.Source.Task.IsCompletedSuccessfully) return (true, entry.Source.Task.Result);

				entry.State = State.TimedOut;
				return (false, default);
			}
		}
	}

	/// <summary>Hands a result to whatever waits under the key. Never waits.</summary>
	/// <returns>What became of the result, and the stored row id when it is <see cref="PendingOutcome.Stored"/>.</returns>
	public (PendingOutcome Outcome, int Id) Complete(TKey key, TResult result)
	{
		if (!_entries.TryGetValue(key, out var entry)) return (PendingOutcome.Unknown, 0);

		lock (entry.Gate)
		{
			switch (entry.State)
			{
				case State.Waiting:
					entry.Source.TrySetResult(result);
					return (PendingOutcome.Delivered, 0);
				case State.TimedOut:
					entry.Result = result;
					entry.HasResult = true;
					return (PendingOutcome.Held, 0);
				default:
					_entries.TryRemove(key, out _);
					return (PendingOutcome.Stored, entry.Id);
			}
		}
	}

	/// <summary>Says that the row of a key that gave up is now stored under <paramref name="id"/>.</summary>
	/// <returns>The result that arrived meanwhile, if any; the caller writes it to the row.</returns>
	public (bool Arrived, TResult? Result) Store(TKey key, int id)
	{
		if (!_entries.TryGetValue(key, out var entry)) return (false, default);

		lock (entry.Gate)
		{
			if (entry.HasResult)
			{
				_entries.TryRemove(key, out _);
				return (true, entry.Result);
			}

			entry.State = State.Stored;
			entry.Id = id;
			return (false, default);
		}
	}

	/// <summary>Forgets a key.</summary>
	public void Discard(TKey key)
	{
		_entries.TryRemove(key, out _);
	}

	private enum State : byte
	{
		Waiting,
		TimedOut,
		Stored
	}

	private sealed class Entry
	{
		public readonly Lock Gate = new();

		public readonly TaskCompletionSource<TResult> Source =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public State State;

		public int Id;

		public bool HasResult;

		public TResult? Result;
	}
}