namespace Basil.Infrastructure.Common.Memory;

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
/// <typeparam name="TKey">The secondary identity of an item.</typeparam>
/// <typeparam name="T">The type of each live item.</typeparam>
public sealed class WeakIndex<TKey, T> : WeakDictionary<TKey, T>
	where TKey : notnull where T : class
{
	/// <summary>Indexes a live instance under a key, replacing whatever the key pointed to.</summary>
	/// <param name="key">The key to index the item under.</param>
	/// <param name="item">The live instance.</param>
	public void Set(TKey key, T item)
	{
		References[key] = new WeakReference<T>(item);
		Tick();
	}

	/// <summary>Forgets a key when it still points to the item.</summary>
	/// <param name="key">The key the item was indexed under.</param>
	/// <param name="item">The item expected under the key.</param>
	public void Remove(TKey key, T item)
	{
		if (References.TryGetValue(key, out var reference) && reference.TryGetTarget(out var current) &&
		    ReferenceEquals(current, item))
			References.TryRemove(new KeyValuePair<TKey, WeakReference<T>>(key, reference));
	}
}