using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Basil.Infrastructure.Storage.Memory;

/// <summary>Keeps the one live instance of each stored item, by key, for as long as anyone holds it or a change of it is not stored yet.</summary>
/// <remarks>
///     Objects nobody holds are released and loaded again on the next request; an item with a change not yet stored is
///     kept.
/// </remarks>
internal sealed class IdentityMap<TKey, T> where TKey : notnull where T : class
{
	// ponytail: dead weak references are swept every SweepEvery additions, an O(n) pass; a timer is only useful when
	// many keys are added rarely.
	private const int SweepEvery = 1024;

	private readonly ConcurrentDictionary<TKey, WeakReference<T>> _instances = new();
	private readonly ConcurrentDictionary<TKey, Lazy<Task<T?>>> _loading = new();
	private readonly ConcurrentDictionary<Task, T> _pinned = new();
	private int _additions;

	/// <summary>Gets the live instance of a key, loading it once however many callers ask at the same time.</summary>
	/// <param name="key">The item's key.</param>
	/// <param name="load">Loads the item; it runs once for all concurrent callers and no caller's token stops it.</param>
	/// <param name="cancellationToken">Stops this caller's wait only.</param>
	/// <returns>The live instance, or <see langword="null" /> when the loader finds nothing (which is not kept).</returns>
	public async ValueTask<T?> GetOrAddAsync(TKey key, Func<TKey, Task<T?>> load, CancellationToken cancellationToken = default)
	{
		if (TryGetValue(key, out var live))
			return live;

		var loading = _loading.GetOrAdd(key, key =>
			new Lazy<Task<T?>>(() => load(key), LazyThreadSafetyMode.ExecutionAndPublication));
		var task = loading.Value;
		_ = task.ContinueWith(_ => _loading.TryRemove(new KeyValuePair<TKey, Lazy<Task<T?>>>(key, loading)),
			TaskScheduler.Default);
		var loaded = await task.WaitAsync(cancellationToken);
		return loaded is null ? null : GetOrAdd(key, loaded);
	}

	/// <summary>Keeps an item unless the key already has a live instance, and returns the live one.</summary>
	public T GetOrAdd(TKey key, T item)
	{
		while (true)
		{
			if (TryGetValue(key, out var live))
				return live;

			var reference = new WeakReference<T>(item);
			if (_instances.TryAdd(key, reference) ||
			    _instances.TryGetValue(key, out var dead) && !dead.TryGetTarget(out _) &&
			    _instances.TryUpdate(key, reference, dead))
			{
				if (Interlocked.Increment(ref _additions) % SweepEvery == 0)
					Sweep();
				return item;
			}
		}
	}

	/// <summary>Forgets the instance of a key.</summary>
	public bool Remove(TKey key)
	{
		return _instances.TryRemove(key, out _);
	}

	/// <summary>Keeps an item alive until a change of it is stored, even when nobody else holds it.</summary>
	public void Pin(T item, Task stored)
	{
		if (stored.IsCompleted)
			return;

		_pinned[stored] = item;
		_ = stored.ContinueWith(completed => _pinned.TryRemove(stored, out _), TaskScheduler.Default);
	}

	public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out T value)
	{
		if (_instances.TryGetValue(key, out var reference))
		{
			if (reference.TryGetTarget(out value))
				return true;

			_instances.TryRemove(new KeyValuePair<TKey, WeakReference<T>>(key, reference));
		}

		value = null;
		return false;
	}

	/// <summary>Gets the live instances.</summary>
	public IEnumerable<T> Values
	{
		get
		{
			foreach (var (key, reference) in _instances)
			{
				if (reference.TryGetTarget(out var value))
					yield return value;
				else
					_instances.TryRemove(new KeyValuePair<TKey, WeakReference<T>>(key, reference));
			}
		}
	}

	private void Sweep()
	{
		foreach (var (key, reference) in _instances)
			if (!reference.TryGetTarget(out _))
				_instances.TryRemove(new KeyValuePair<TKey, WeakReference<T>>(key, reference));
	}
}
