using System.Collections;
using System.Collections.Concurrent;

namespace Basil.Domain;

/// <summary>
///     Represents a thread-safe set of unique values that can be accessed by multiple threads concurrently.
/// </summary>
/// <typeparam name="T">The type of elements in the set.</typeparam>
public sealed class ConcurrentSet<T> : ISet<T> where T : notnull
{
	private const byte Present = 0;
	private readonly ConcurrentDictionary<T, byte> _items = new();

	public int Count => _items.Count;

	public bool IsReadOnly => false;

	public bool Add(T item)
	{
		return _items.TryAdd(item, Present);
	}

	void ICollection<T>.Add(T item)
	{
		Add(item);
	}

	public bool Remove(T item)
	{
		return _items.TryRemove(item, out _);
	}

	public bool Contains(T item)
	{
		return _items.ContainsKey(item);
	}

	public void Clear()
	{
		_items.Clear();
	}

	public void CopyTo(T[] array, int arrayIndex)
	{
		_items.Keys.CopyTo(array, arrayIndex);
	}

	public void UnionWith(IEnumerable<T> other)
	{
		foreach (var item in other) Add(item);
	}

	public void ExceptWith(IEnumerable<T> other)
	{
		foreach (var item in other) Remove(item);
	}

	public void IntersectWith(IEnumerable<T> other)
	{
		var otherSet = other.ToHashSet();

		foreach (var item in _items.Keys)
			if (!otherSet.Contains(item))
				Remove(item);
	}

	public void SymmetricExceptWith(IEnumerable<T> other)
	{
		foreach (var item in other)
			if (!Remove(item))
				Add(item);
	}

	public bool IsSubsetOf(IEnumerable<T> other)
	{
		return _items.Keys.ToHashSet().IsSubsetOf(other);
	}

	public bool IsSupersetOf(IEnumerable<T> other)
	{
		return _items.Keys.ToHashSet().IsSupersetOf(other);
	}

	public bool IsProperSubsetOf(IEnumerable<T> other)
	{
		return _items.Keys.ToHashSet().IsProperSubsetOf(other);
	}

	public bool IsProperSupersetOf(IEnumerable<T> other)
	{
		return _items.Keys.ToHashSet().IsProperSupersetOf(other);
	}

	public bool Overlaps(IEnumerable<T> other)
	{
		return _items.Keys.ToHashSet().Overlaps(other);
	}

	public bool SetEquals(IEnumerable<T> other)
	{
		return _items.Keys.ToHashSet().SetEquals(other);
	}

	public IEnumerator<T> GetEnumerator()
	{
		return _items.Keys.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}
}