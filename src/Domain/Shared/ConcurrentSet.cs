using System.Collections;
using System.Collections.Concurrent;

namespace Basil.Domain.Shared;

public sealed class ConcurrentSet<T> : ISet<T>, IReadOnlySet<T> where T : notnull
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
		foreach (var item in this)
			array[arrayIndex++] = item;
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

		foreach (var item in this)
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
		return this.ToHashSet().IsSubsetOf(other);
	}

	public bool IsSupersetOf(IEnumerable<T> other)
	{
		return this.ToHashSet().IsSupersetOf(other);
	}

	public bool IsProperSubsetOf(IEnumerable<T> other)
	{
		return this.ToHashSet().IsProperSubsetOf(other);
	}

	public bool IsProperSupersetOf(IEnumerable<T> other)
	{
		return this.ToHashSet().IsProperSupersetOf(other);
	}

	public bool Overlaps(IEnumerable<T> other)
	{
		return this.ToHashSet().Overlaps(other);
	}

	public bool SetEquals(IEnumerable<T> other)
	{
		return this.ToHashSet().SetEquals(other);
	}

	public IEnumerator<T> GetEnumerator()
	{
		return _items.Select(pair => pair.Key).GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}
}