using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Basil.Infrastructure.Storage.Memory;

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
internal sealed class IdentityMap<TKey, T> : IDictionary<TKey, T>, IReadOnlyDictionary<TKey, T>
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
	public int Count
	{
		get
		{
			var count = 0;
			foreach (var (key, reference) in _instances)
				if (reference.TryGetTarget(out _))
					count++;
				else
					_instances.TryRemove(new KeyValuePair<TKey, WeakReference<T>>(key, reference));
			return count;
		}
	}

	/// <summary>Gets the keys that currently have a live instance.</summary>
	public ICollection<TKey> Keys => Snapshot().Select(entry => entry.Key).ToArray();

	/// <summary>Gets the live instances.</summary>
	public ICollection<T> Values => Snapshot().Select(entry => entry.Value).ToArray();

	IEnumerable<TKey> IReadOnlyDictionary<TKey, T>.Keys => Keys;

	IEnumerable<T> IReadOnlyDictionary<TKey, T>.Values => Values;

	/// <summary>Gets a value that indicates whether the map can be changed.</summary>
	/// <value>Always <see langword="false" />; the map is never read-only.</value>
	public bool IsReadOnly => false;

	/// <summary>Gets the live instance of a key, or keeps an item under a key.</summary>
	/// <param name="key">The item's key.</param>
	/// <returns>The live instance.</returns>
	/// <exception cref="KeyNotFoundException">The key has no live instance.</exception>
	public T this[TKey key]
	{
		get => TryGetValue(key, out var value)
			? value
			: throw new KeyNotFoundException($"The key {key} has no live instance.");
		set => Store(key, value);
	}

	/// <summary>Keeps an item under a key; the key must not already have a live instance.</summary>
	/// <param name="key">The item's key.</param>
	/// <param name="item">The item.</param>
	/// <exception cref="ArgumentException">The key already has a live instance.</exception>
	public void Add(TKey key, T item)
	{
		var reference = new WeakReference<T>(item);
		while (true)
		{
			if (_instances.TryGetValue(key, out var existing))
			{
				if (existing.TryGetTarget(out _))
					throw new ArgumentException($"The key {key} already has a live instance.", nameof(key));
				if (_instances.TryUpdate(key, reference, existing))
					break;
			}
			else if (_instances.TryAdd(key, reference))
			{
				break;
			}
		}

		Tick();
	}

	/// <summary>Keeps the item of a pair under its key.</summary>
	/// <param name="item">The pair to add.</param>
	public void Add(KeyValuePair<TKey, T> item)
	{
		Add(item.Key, item.Value);
	}

	/// <summary>Forgets the instance of a key.</summary>
	/// <param name="key">The item's key.</param>
	/// <returns><see langword="true" /> when the key had an entry.</returns>
	public bool Remove(TKey key)
	{
		return _instances.TryRemove(key, out _);
	}

	/// <summary>Forgets the instance of a key when it is still the given item.</summary>
	/// <param name="item">The key and the item expected under it.</param>
	/// <returns><see langword="true" /> when the entry was removed.</returns>
	public bool Remove(KeyValuePair<TKey, T> item)
	{
		if (!TryGetValue(item.Key, out var value) || !EqualityComparer<T>.Default.Equals(value, item.Value))
			return false;

		return Remove(item.Key);
	}

	/// <summary>Forgets every key.</summary>
	public void Clear()
	{
		_instances.Clear();
	}

	/// <summary>Gets a value that indicates whether a key has a live instance.</summary>
	/// <param name="key">The key to look up.</param>
	/// <returns><see langword="true" /> if the key has a live instance; otherwise, <see langword="false" />.</returns>
	public bool ContainsKey(TKey key)
	{
		return TryGetValue(key, out _);
	}

	/// <summary>Gets a value that indicates whether the map keeps a given item under its key.</summary>
	/// <param name="item">The key and the item expected under it.</param>
	/// <returns><see langword="true" /> if the map holds the item; otherwise, <see langword="false" />.</returns>
	public bool Contains(KeyValuePair<TKey, T> item)
	{
		return TryGetValue(item.Key, out var value) && EqualityComparer<T>.Default.Equals(value, item.Value);
	}

	/// <summary>Copies the live key-value pairs into an array.</summary>
	/// <param name="array">The destination array.</param>
	/// <param name="arrayIndex">The zero-based index in <paramref name="array" /> at which copying begins.</param>
	public void CopyTo(KeyValuePair<TKey, T>[] array, int arrayIndex)
	{
		Snapshot().CopyTo(array, arrayIndex);
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

	/// <summary>Keeps an item under a key, replacing any instance the key held.</summary>
	private void Store(TKey key, T item)
	{
		_instances[key] = new WeakReference<T>(item);
		Tick();
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
