using System.Collections.Concurrent;

namespace Basil.Infrastructure.Common.Registry;

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
/// <typeparam name="TKey">The identity of an item.</typeparam>
/// <typeparam name="T">The type of each item.</typeparam>
public sealed class IdentityMap<TKey, T> : WeakDictionary<TKey, T>
	where TKey : notnull where T : class
{
	private readonly ConcurrentDictionary<TKey, Lazy<Task<T?>>> _loading = new();
	private readonly ConcurrentDictionary<Task, T> _pinned = new();

	/// <summary>Forgets the instance of a key.</summary>
	/// <param name="key">The item's key.</param>
	/// <returns><see langword="true" /> when the key had an entry.</returns>
	public bool Remove(TKey key)
	{
		return References.TryRemove(key, out _);
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

		// The factory must always return a task: a load that fails synchronously leaves its exception cached in the
		// Lazy under ExecutionAndPublication, so later callers could never reach the loader again.
		var loading = _loading.GetOrAdd(key, k =>
			new Lazy<Task<T?>>(async () => await load(k), LazyThreadSafetyMode.ExecutionAndPublication));
		var task = loading.Value;
		_ = task.ContinueWith(
			_ => _loading.TryRemove(new KeyValuePair<TKey, Lazy<Task<T?>>>(key, loading)),
			CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
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
			if (References.TryAdd(key, reference) ||
			    (References.TryGetValue(key, out var dead) && !dead.TryGetTarget(out _) &&
			     References.TryUpdate(key, reference, dead)))
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
}