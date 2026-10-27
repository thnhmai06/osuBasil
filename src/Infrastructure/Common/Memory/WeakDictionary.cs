using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Basil.Infrastructure.Common.Memory;

/// <summary>Holds the items that are live at the moment, each under a key, without keeping an item alive.</summary>
/// <remarks>
///     An entry whose item nobody holds any more is absent: it is dropped when it is looked up, when the entries are
///     enumerated, and once enough entries have been added.
/// </remarks>
/// <typeparam name="TKey">The identity of a live item.</typeparam>
/// <typeparam name="T">The type of each live item.</typeparam>
public abstract class WeakDictionary<TKey, T> : IReadOnlyDictionary<TKey, T>
	where TKey : notnull where T : class
{
	// Dead weak references are swept every SweepEvery additions, an O(n) pass; a timer is only useful when many keys
	// are added rarely.
	private const int SweepEvery = 1024;

	private int _additions;

	/// <summary>Gets the entries for the live items held by the derived collection.</summary>
	protected ConcurrentDictionary<TKey, WeakReference<T>> References { get; } = new();

	/// <summary>Gets the number of keys that currently have a live instance.</summary>
	public int Count => References.Values.Count(reference => reference.TryGetTarget(out _));

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

	/// <summary>Gets the live instance of a key.</summary>
	/// <param name="key">The key to look up.</param>
	/// <param name="item">The live instance.</param>
	/// <returns><see langword="true" /> when the key has a live instance; otherwise, <see langword="false" />.</returns>
	public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out T item)
	{
		if (References.TryGetValue(key, out var reference))
		{
			if (reference.TryGetTarget(out item))
				return true;

			References.TryRemove(new KeyValuePair<TKey, WeakReference<T>>(key, reference));
		}

		item = null;
		return false;
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

	/// <summary>Records an addition, sweeping the dead references once enough of them have piled up.</summary>
	protected void Tick()
	{
		if (Interlocked.Increment(ref _additions) % SweepEvery == 0)
			Sweep();
	}

	/// <summary>Snapshots the live entries, dropping references whose item is gone.</summary>
	private List<KeyValuePair<TKey, T>> Snapshot()
	{
		var entries = new List<KeyValuePair<TKey, T>>();
		foreach (var (key, reference) in References)
			if (reference.TryGetTarget(out var value))
				entries.Add(new KeyValuePair<TKey, T>(key, value));
			else
				References.TryRemove(new KeyValuePair<TKey, WeakReference<T>>(key, reference));
		return entries;
	}

	private void Sweep()
	{
		foreach (var (key, reference) in References)
			if (!reference.TryGetTarget(out _))
				References.TryRemove(new KeyValuePair<TKey, WeakReference<T>>(key, reference));
	}
}