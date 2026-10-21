using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Basil.Infrastructure.Persistence.Memory;

/// <summary>
///     Keeps the one live instance of each stored item, by key, for as long as anyone holds it or a change of it is
///     not stored yet.
/// </summary>
/// <remarks>
///     <para>
///         Objects nobody holds are released and loaded again on the next request; an item with a change not yet
///         stored is kept.
///     </para>
///     <para>
///         The map holds the instances that are live at the moment, not every stored item: a key whose item nobody
///         holds any more is absent, so the contents can shrink without <see cref="Remove(TKey)" /> being called. A
///         caller that needs every stored item asks the repository, which loads the missing ones.
///     </para>
/// </remarks>
internal sealed class IdentityMap<TKey, T> : IReadOnlyDictionary<TKey, T>
	where TKey : notnull where T : class
{
	// ponytail: dead weak references are swept every SweepEvery additions, an O(n) pass;
	// a timer is only useful when many keys are added rarely.
	private const int SweepEvery = 1024;

	private readonly ConcurrentDictionary<TKey, WeakReference<T>> _instances = new();
	private readonly ConcurrentDictionary<TKey, Lazy<Task<T?>>> _loading = new();
	private readonly ConcurrentDictionary<Task, T> _pinned = new();
	private int _additions;

	/// <summary>Gets the number of keys that currently have a live instance.</summary>
	public int Count => _instances.Values.Count(reference => reference.TryGetTarget(out _));

	/// <summary>Gets the keys that currently have a live instance.</summary>
	public IEnumerable<TKey> Keys => Snapshot().Select(entry => entry.Key);

	/// <summary>Gets the live instances.</summary>
	public IEnumerable<T> Values => Snapshot().Select(entry => entry.Value);

	/// <summary>Gets the live instance of a key.</summary>
	/// <param name="key">The item's key.</param>
	/// <returns>The live instance.</returns>
	/// <exception cref="KeyNotFoundException">The key has no live instance.</exception>
	public T this[TKey key] => TryGetValue(key, out var value)
		? value
		: throw new KeyNotFoundException($"The key {key} has no live instance.");

	/// <summary>Gets a value that indicates whether a key has a live instance.</summary>
	/// <param name="key">The key to look up.</param>
	/// <returns><see langword="true" /> if the key has a live instance; otherwise, <see langword="false" />.</returns>
	public bool ContainsKey(TKey key)
	{
		return TryGetValue(key, out _);
	}

	/// <summary>Returns an enumerator over the live key-value pairs.</summary>
	/// <returns>An enumerator over the live entries.</returns>
	public IEnumerator<KeyValuePair<TKey, T>> GetEnumerator()
	{
		return Snapshot().GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}

	/// <summary>Gets the live instance of a key.</summary>
	/// <param name="key">The item's key.</param>
	/// <param name="value">The live instance.</param>
	/// <returns><see langword="true" /> when the key has a live instance; otherwise, <see langword="false" />.</returns>
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

	/// <summary>Forgets the instance of a key.</summary>
	/// <param name="key">The item's key.</param>
	/// <returns><see langword="true" /> when the key had an entry.</returns>
	public bool Remove(TKey key)
	{
		return _instances.TryRemove(key, out _);
	}

	/// <summary>Gets the live instance of a key, loading it once however many callers ask at the same time.</summary>
	/// <param name="key">The item's key.</param>
	/// <param name="load">Loads the item; it runs once for all concurrent callers and no caller's token stops it.</param>
	/// <param name="cancellationToken">Stops this caller's wait only.</param>
	/// <returns>The live instance, or <see langword="null" /> when the loader finds nothing (which is not kept).</returns>
	public async ValueTask<T?> GetOrAddAsync(TKey key, Func<TKey, Task<T?>> load,
		CancellationToken cancellationToken = default)
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
	/// <param name="key">The item's key.</param>
	/// <param name="item">The item to keep when the key has none.</param>
	/// <returns>The live instance.</returns>
	public T GetOrAdd(TKey key, T item)
	{
		while (true)
		{
			if (TryGetValue(key, out var live))
				return live;

			var reference = new WeakReference<T>(item);
			if (_instances.TryAdd(key, reference) ||
			    (_instances.TryGetValue(key, out var dead) && !dead.TryGetTarget(out _) &&
			     _instances.TryUpdate(key, reference, dead)))
			{
				Tick();
				return item;
			}
		}
	}

	/// <summary>Keeps an item alive until a change of it is stored, even when nobody else holds it.</summary>
	/// <param name="item">The item to keep alive.</param>
	/// <param name="stored">Completes once the change is stored.</param>
	public void Pin(T item, Task stored)
	{
		if (stored.IsCompleted)
			return;

		_pinned[stored] = item;
		_ = stored.ContinueWith(completed => _pinned.TryRemove(stored, out _), TaskScheduler.Default);
	}

	/// <summary>Snapshots the live entries, dropping references whose item is gone.</summary>
	private List<KeyValuePair<TKey, T>> Snapshot()
	{
		var entries = new List<KeyValuePair<TKey, T>>();
		foreach (var (key, reference) in _instances)
			if (reference.TryGetTarget(out var value))
				entries.Add(new KeyValuePair<TKey, T>(key, value));
			else
				_instances.TryRemove(new KeyValuePair<TKey, WeakReference<T>>(key, reference));
		return entries;
	}

	private void Tick()
	{
		if (Interlocked.Increment(ref _additions) % SweepEvery == 0)
			Sweep();
	}

	private void Sweep()
	{
		foreach (var (key, reference) in _instances)
			if (!reference.TryGetTarget(out _))
				_instances.TryRemove(new KeyValuePair<TKey, WeakReference<T>>(key, reference));
	}
}