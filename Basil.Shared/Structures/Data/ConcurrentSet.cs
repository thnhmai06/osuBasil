using System.Collections;
using System.Collections.Concurrent;

namespace Basil.Shared.Structures.Data;

/// <summary>A set that may be read while other threads change it.</summary>
/// <remarks>
///     It is deliberately not an <see cref="ICollection{T}" />: a copy made through that interface sizes its target from
///     <see cref="Count" /> first, and a member added in between overflows it. A copy therefore enumerates the set.
///     A collection expression that spreads the set also sizes from <see cref="Count" />; spread <c>set.ToArray()</c>.
/// </remarks>
public class ConcurrentSet<T> : IReadOnlySet<T> where T : notnull
{
	private const byte Present = 0;
	private readonly ConcurrentDictionary<T, byte> _items = new();

	public int Count => _items.Count;

	public bool Add(T item)
	{
		return _items.TryAdd(item, Present);
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