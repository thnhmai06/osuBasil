using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Basil.Infrastructure.Storage.Memory;

/// <summary>Finds live instances by a second unique key, such as a user's safe name or a beatmap's hash, for as long as they live.</summary>
/// <remarks>The key of an item can change after it was indexed, so a caller checks the key on the item it gets.</remarks>
internal sealed class WeakIndex<TKey, T> where TKey : notnull where T : class
{
	// ponytail: dead weak references are swept every SweepEvery additions, an O(n) pass; a timer is only useful when
	// many keys are added rarely.
	private const int SweepEvery = 1024;
	private readonly ConcurrentDictionary<TKey, WeakReference<T>> _items = new();
	private int _additions;

	public void Set(TKey key, T item)
	{
		_items[key] = new WeakReference<T>(item);
		if (Interlocked.Increment(ref _additions) % SweepEvery == 0)
			Sweep();
	}

	public bool TryGet(TKey key, [MaybeNullWhen(false)] out T item)
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
		if (_items.TryGetValue(key, out var reference) && reference.TryGetTarget(out var current) && ReferenceEquals(current, item))
			_items.TryRemove(new KeyValuePair<TKey, WeakReference<T>>(key, reference));
	}

	private void Sweep()
	{
		foreach (var (key, reference) in _items)
			if (!reference.TryGetTarget(out _))
				_items.TryRemove(new KeyValuePair<TKey, WeakReference<T>>(key, reference));
	}
}
