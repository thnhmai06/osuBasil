using System.Collections;
using System.Collections.Concurrent;

namespace Basil.Domain;

/// <summary>
///     Represents a thread-safe set of unique values.
/// </summary>
/// <typeparam name="T">The type of elements in the set.</typeparam>
public sealed class ConcurrentSet<T> : ISet<T> where T : notnull
{
	private const byte Present = 0;
	private readonly ConcurrentDictionary<T, byte> _items = new();

	/// <summary>
	///     Gets the number of elements contained in the set.
	/// </summary>
	public int Count => _items.Count;

	/// <summary>
	///     Gets a value indicating whether the set is read-only.
	/// </summary>
	public bool IsReadOnly => false;

	/// <summary>
	///     Adds an element to the set.
	/// </summary>
	/// <param name="item">The element to add.</param>
	/// <returns>
	///     <see langword="true" /> if the element was added; otherwise,
	///     <see langword="false" /> if the element was already present.
	/// </returns>
	public bool Add(T item)
	{
		return _items.TryAdd(item, Present);
	}

	/// <summary>
	///     Adds an element to the set.
	/// </summary>
	/// <param name="item">The element to add.</param>
	void ICollection<T>.Add(T item)
	{
		Add(item);
	}

	/// <summary>
	///     Removes an element from the set.
	/// </summary>
	/// <param name="item">The element to remove.</param>
	/// <returns>
	///     <see langword="true" /> if the element was removed; otherwise,
	///     <see langword="false" /> if the element was not present.
	/// </returns>
	public bool Remove(T item)
	{
		return _items.TryRemove(item, out _);
	}

	/// <summary>
	///     Determines whether the set contains a specific element.
	/// </summary>
	/// <param name="item">The element to locate.</param>
	/// <returns>
	///     <see langword="true" /> if the element is present; otherwise,
	///     <see langword="false" />.
	/// </returns>
	public bool Contains(T item)
	{
		return _items.ContainsKey(item);
	}

	/// <summary>
	///     Removes all elements from the set.
	/// </summary>
	public void Clear()
	{
		_items.Clear();
	}

	/// <summary>
	///     Copies the elements of the set to an array.
	/// </summary>
	/// <param name="array">The destination array.</param>
	/// <param name="arrayIndex">The zero-based index at which copying begins.</param>
	public void CopyTo(T[] array, int arrayIndex)
	{
		_items.Keys.CopyTo(array, arrayIndex);
	}

	/// <summary>
	///     Adds all elements from the specified collection to the set.
	/// </summary>
	/// <param name="other">The collection whose elements are added to the set.</param>
	public void UnionWith(IEnumerable<T> other)
	{
		foreach (var item in other) Add(item);
	}

	/// <summary>
	///     Removes all elements from the set that are present in the specified collection.
	/// </summary>
	/// <param name="other">The collection whose elements are removed from the set.</param>
	public void ExceptWith(IEnumerable<T> other)
	{
		foreach (var item in other) Remove(item);
	}

	/// <summary>
	///     Removes all elements from the set that are not present in the specified collection.
	/// </summary>
	/// <param name="other">The collection used to determine the intersection.</param>
	public void IntersectWith(IEnumerable<T> other)
	{
		var otherSet = other.ToHashSet();

		foreach (var item in _items.Keys)
			if (!otherSet.Contains(item))
				Remove(item);
	}

	/// <summary>
	///     Modifies the set to contain only elements that are present in exactly one of the two collections.
	/// </summary>
	/// <param name="other">The collection to compare with the set.</param>
	public void SymmetricExceptWith(IEnumerable<T> other)
	{
		foreach (var item in other)
			if (!Remove(item))
				Add(item);
	}

	/// <summary>
	///     Determines whether the set is a subset of the specified collection.
	/// </summary>
	/// <param name="other">The collection to compare with the set.</param>
	/// <returns>
	///     <see langword="true" /> if the set is a subset of <paramref name="other" />; otherwise,
	///     <see langword="false" />.
	/// </returns>
	public bool IsSubsetOf(IEnumerable<T> other)
	{
		return _items.Keys.ToHashSet().IsSubsetOf(other);
	}

	/// <summary>
	///     Determines whether the set is a superset of the specified collection.
	/// </summary>
	/// <param name="other">The collection to compare with the set.</param>
	/// <returns>
	///     <see langword="true" /> if the set is a superset of <paramref name="other" />; otherwise,
	///     <see langword="false" />.
	/// </returns>
	public bool IsSupersetOf(IEnumerable<T> other)
	{
		return _items.Keys.ToHashSet().IsSupersetOf(other);
	}

	/// <summary>
	///     Determines whether the set is a proper subset of the specified collection.
	/// </summary>
	/// <param name="other">The collection to compare with the set.</param>
	/// <returns>
	///     <see langword="true" /> if the set is a proper subset of <paramref name="other" />; otherwise,
	///     <see langword="false" />.
	/// </returns>
	public bool IsProperSubsetOf(IEnumerable<T> other)
	{
		return _items.Keys.ToHashSet().IsProperSubsetOf(other);
	}

	/// <summary>
	///     Determines whether the set is a proper superset of the specified collection.
	/// </summary>
	/// <param name="other">The collection to compare with the set.</param>
	/// <returns>
	///     <see langword="true" /> if the set is a proper superset of <paramref name="other" />; otherwise,
	///     <see langword="false" />.
	/// </returns>
	public bool IsProperSupersetOf(IEnumerable<T> other)
	{
		return _items.Keys.ToHashSet().IsProperSupersetOf(other);
	}

	/// <summary>
	///     Determines whether the set overlaps with the specified collection.
	/// </summary>
	/// <param name="other">The collection to compare with the set.</param>
	/// <returns>
	///     <see langword="true" /> if the set and <paramref name="other" /> have at least one element in common;
	///     otherwise, <see langword="false" />.
	/// </returns>
	public bool Overlaps(IEnumerable<T> other)
	{
		return _items.Keys.ToHashSet().Overlaps(other);
	}

	/// <summary>
	///     Determines whether the set contains the same elements as the specified collection.
	/// </summary>
	/// <param name="other">The collection to compare with the set.</param>
	/// <returns>
	///     <see langword="true" /> if the set and <paramref name="other" /> contain the same elements;
	///     otherwise, <see langword="false" />.
	/// </returns>
	public bool SetEquals(IEnumerable<T> other)
	{
		return _items.Keys.ToHashSet().SetEquals(other);
	}

	/// <summary>
	///     Returns an enumerator that iterates through the elements in the set.
	/// </summary>
	/// <returns>An enumerator for the elements in the set.</returns>
	public IEnumerator<T> GetEnumerator()
	{
		return _items.Keys.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}
}