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

	/// <summary>Gets the number of unique elements currently held in the set.</summary>
	public int Count => _items.Count;

	/// <summary>
	///     Gets a value that indicates whether the set is read-only.
	/// </summary>
	/// <value>Always <see langword="false" />; the set never becomes read-only.</value>
	public bool IsReadOnly => false;

	/// <summary>Adds an element to the set if it is not already present.</summary>
	/// <param name="item">The element to add.</param>
	/// <returns>
	///     <see langword="true" /> if the element was added; <see langword="false" /> if it was
	///     already present.
	/// </returns>
	public bool Add(T item)
	{
		return _items.TryAdd(item, Present);
	}

	void ICollection<T>.Add(T item)
	{
		Add(item);
	}

	/// <summary>Removes an element from the set if it is present.</summary>
	/// <param name="item">The element to remove.</param>
	/// <returns>
	///     <see langword="true" /> if the element was present and removed; otherwise,
	///     <see langword="false" />.
	/// </returns>
	public bool Remove(T item)
	{
		return _items.TryRemove(item, out _);
	}

	/// <summary>Gets a value that indicates whether an element is present in the set.</summary>
	/// <param name="item">The element to look up.</param>
	/// <returns>
	///     <see langword="true" /> if the element is present; otherwise, <see langword="false" />.
	/// </returns>
	public bool Contains(T item)
	{
		return _items.ContainsKey(item);
	}

	/// <summary>Removes all elements from the set.</summary>
	public void Clear()
	{
		_items.Clear();
	}

	/// <summary>Copies the current elements of the set to an array, starting at a specified index.</summary>
	/// <param name="array">The destination array.</param>
	/// <param name="arrayIndex">
	///     The zero-based index in <paramref name="array" /> at which copying begins.
	/// </param>
	/// <remarks>
	///     The copy is taken from the backing dictionary's keys, which are captured without
	///     locking; concurrent modifications during the copy may or may not be reflected.
	/// </remarks>
	public void CopyTo(T[] array, int arrayIndex)
	{
		_items.Keys.CopyTo(array, arrayIndex);
	}

	/// <summary>Adds every element of <paramref name="other" /> to the set.</summary>
	/// <param name="other">The elements to add.</param>
	/// <remarks>
	///     Adding an element that is already present is a no-op, so the set never ends up with
	///     duplicates regardless of <paramref name="other" />.
	/// </remarks>
	public void UnionWith(IEnumerable<T> other)
	{
		foreach (var item in other) Add(item);
	}

	/// <summary>Removes every element of <paramref name="other" /> from the set.</summary>
	/// <param name="other">The elements to remove.</param>
	public void ExceptWith(IEnumerable<T> other)
	{
		foreach (var item in other) Remove(item);
	}

	/// <summary>
	///     Retains only the elements of the set that are also present in <paramref name="other" />.
	/// </summary>
	/// <param name="other">The elements to intersect with.</param>
	/// <remarks>
	///     <paramref name="other" /> is enumerated once into a snapshot before any removal, so a
	///     lazily-evaluated sequence is unaffected by the mutation.
	/// </remarks>
	public void IntersectWith(IEnumerable<T> other)
	{
		var otherSet = other.ToHashSet();

		foreach (var item in _items.Keys)
			if (!otherSet.Contains(item))
				Remove(item);
	}

	/// <summary>
	///     Retains only the elements that are present in exactly one of the set and
	///     <paramref name="other" />.
	/// </summary>
	/// <param name="other">The elements to toggle.</param>
	public void SymmetricExceptWith(IEnumerable<T> other)
	{
		foreach (var item in other)
			if (!Remove(item))
				Add(item);
	}

	/// <summary>
	///     Gets a value that indicates whether every element of the set is also in
	///     <paramref name="other" />.
	/// </summary>
	/// <param name="other">The elements to compare against.</param>
	/// <returns>
	///     <see langword="true" /> if the set is a subset of <paramref name="other" />; otherwise,
	///     <see langword="false" />.
	/// </returns>
	public bool IsSubsetOf(IEnumerable<T> other)
	{
		return _items.Keys.ToHashSet().IsSubsetOf(other);
	}

	/// <summary>
	///     Gets a value that indicates whether every element of <paramref name="other" /> is in the
	///     set.
	/// </summary>
	/// <param name="other">The elements to compare against.</param>
	/// <returns>
	///     <see langword="true" /> if the set is a superset of <paramref name="other" />; otherwise,
	///     <see langword="false" />.
	/// </returns>
	public bool IsSupersetOf(IEnumerable<T> other)
	{
		return _items.Keys.ToHashSet().IsSupersetOf(other);
	}

	/// <summary>
	///     Gets a value that indicates whether the set is a proper subset of
	///     <paramref name="other" /> -- every element of the set is also in
	///     <paramref name="other" />, and the two are not equal.
	/// </summary>
	/// <param name="other">The elements to compare against.</param>
	/// <returns>
	///     <see langword="true" /> if the set is a proper subset of <paramref name="other" />;
	///     otherwise, <see langword="false" />.
	/// </returns>
	public bool IsProperSubsetOf(IEnumerable<T> other)
	{
		return _items.Keys.ToHashSet().IsProperSubsetOf(other);
	}

	/// <summary>
	///     Gets a value that indicates whether the set is a proper superset of
	///     <paramref name="other" /> -- every element of <paramref name="other" /> is in the set,
	///     and the two are not equal.
	/// </summary>
	/// <param name="other">The elements to compare against.</param>
	/// <returns>
	///     <see langword="true" /> if the set is a proper superset of <paramref name="other" />;
	///     otherwise, <see langword="false" />.
	/// </returns>
	public bool IsProperSupersetOf(IEnumerable<T> other)
	{
		return _items.Keys.ToHashSet().IsProperSupersetOf(other);
	}

	/// <summary>
	///     Gets a value that indicates whether the set and <paramref name="other" /> share at least
	///     one element.
	/// </summary>
	/// <param name="other">The elements to compare against.</param>
	/// <returns>
	///     <see langword="true" /> if the set and <paramref name="other" /> have at least one
	///     common element; otherwise, <see langword="false" />.
	/// </returns>
	public bool Overlaps(IEnumerable<T> other)
	{
		return _items.Keys.ToHashSet().Overlaps(other);
	}

	/// <summary>
	///     Gets a value that indicates whether the set and <paramref name="other" /> contain
	///     exactly the same elements.
	/// </summary>
	/// <param name="other">The elements to compare against.</param>
	/// <returns>
	///     <see langword="true" /> if the two collections contain the same elements; otherwise,
	///     <see langword="false" />.
	/// </returns>
	public bool SetEquals(IEnumerable<T> other)
	{
		return _items.Keys.ToHashSet().SetEquals(other);
	}

	/// <summary>
	///     Returns an enumerator over the set's elements.
	/// </summary>
	/// <returns>An enumerator over the elements currently in the set.</returns>
	/// <remarks>
	///     Enumeration is thread-safe and weakly consistent: it never throws on concurrent
	///     modification, but it may or may not include elements added or removed while iterating.
	/// </remarks>
	public IEnumerator<T> GetEnumerator()
	{
		return _items.Keys.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}
}