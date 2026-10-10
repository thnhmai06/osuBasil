using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Basil.Infrastructure.Storage.Memory;

/// <summary>
///     Finds live instances by a second unique key, such as a user's safe name or a beatmap's hash, for as long as
///     they live.
/// </summary>
/// <remarks>
///     <para>The key of an item can change after it was indexed, so a caller checks the key on the item it gets.</para>
///     <para>
///         The index holds the instances that are live at the moment, not every stored item: a key whose item nobody
///         holds any more is absent. Its contents are a hint, not an authoritative list of stored items.
///     </para>
/// </remarks>
internal sealed class WeakIndex<TKey, T> : IReadOnlyDictionary<TKey, T>
	where TKey : notnull where T : class
{
	// ponytail: dead weak references are swept every SweepEvery additions, an O(n) pass; a timer is only useful when
	// many keys are added rarely.
	private const int SweepEvery = 1024;
	private readonly ConcurrentDictionary<TKey, WeakReference<T>> _items = new();
	private int _additions;

	/// <summary>Gets the number of keys that currently have a live instance.</summary>
	public int Count
	{
		get
		{
			var count = 0;
			foreach (var (key, reference) in _items)
				if (reference.TryGetTarget(out _))
					count++;
				else
					_items.TryRemove(new KeyValuePair<TKey, WeakReference<T>>(key, reference));
			return count;
		}
	}

	/// <summary>Gets the keys that currently have a live instance.</summary>
	public IEnumerable<TKey> Keys => Snapshot().Select(entry => entry.Key);

	/// <summary>Gets the live instances.</summary>
	public IEnumerable<T> Values => Snapshot().Select(entry => entry.Value);

	/// <summary>Gets the live instance of a key.</summary>
	/// <param name="key">The key the item was indexed under.</param>
	/// <returns>The live instance.</returns>
	/// <exception cref="KeyNotFoundException">The key has no live instance.</exception>
	public T this[TKey key] => TryGetValue(key, out var value)
		? value
		: throw new KeyNotFoundException($"The key {key} has no live instance.");

	public void Set(TKey key, T item)
	{
		_items[key] = new WeakReference<T>(item);
		if (Interlocked.Increment(ref _additions) % SweepEvery == 0)
			Sweep();
	}

	/// <summary>Gets a value that indicates whether a key has a live instance.</summary>
	/// <param name="key">The key to look up.</param>
	/// <returns><see langword="true" /> if the key has a live instance; otherwise, <see langword="false" />.</returns>
	public bool ContainsKey(TKey key)
	{
		return TryGetValue(key, out _);
	}

	public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out T item)
	{
		if (_items.TryGetValue(key, out var reference))
		{
			if (reference.TryGetTarget(out item))
				return true;

			_items.TryRemove(new KeyValuePair<TKey, WeakReference<T>>(key, reference));
		}

		item = null;
		return false;
	}

	/// <summary>Forgets a key when it still points to the item.</summary>
	public void Remove(TKey key, T item)
	{
		if (_items.TryGetValue(key, out var reference) && reference.TryGetTarget(out var current) &&
		    ReferenceEquals(current, item))
			_items.TryRemove(new KeyValuePair<TKey, WeakReference<T>>(key, reference));
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

	/// <summary>Snapshots the live entries, dropping references whose item is gone.</summary>
	private List<KeyValuePair<TKey, T>> Snapshot()
	{
		var entries = new List<KeyValuePair<TKey, T>>();
		foreach (var (key, reference) in _items)
			if (reference.TryGetTarget(out var value))
				entries.Add(new KeyValuePair<TKey, T>(key, value));
			else
				_items.TryRemove(new KeyValuePair<TKey, WeakReference<T>>(key, reference));
		return entries;
	}

	private void Sweep()
	{
		foreach (var (key, reference) in _items)
			if (!reference.TryGetTarget(out _))
				_items.TryRemove(new KeyValuePair<TKey, WeakReference<T>>(key, reference));
	}
}