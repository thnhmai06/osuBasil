using Microsoft.Extensions.Caching.Memory;

namespace Basil.Infrastructure.Common.Registry;

/// <summary>
///     Keeps small, often read values next to the database that holds them: a read fills the cache on a miss, and a
///     write that succeeded drops the entry.
/// </summary>
/// <typeparam name="TKey">The type of the key.</typeparam>
/// <typeparam name="TValue">The type of the cached value.</typeparam>
/// <remarks>
///     A write must call <see cref="Remove" /> only after the database accepted it, so a refused write leaves the cache
///     unchanged. A value that was loaded while an entry was being dropped is not cached. The cache holds one entry per
///     key read and has no size limit; use it for one small value per account.
/// </remarks>
public sealed class CacheAside<TKey, TValue>(IMemoryCache cache) where TKey : notnull where TValue : class
{
	private readonly Lock _gate = new();
	private long _removals;

	/// <summary>Gets the cached value, or loads it from the database and caches it.</summary>
	/// <param name="key">The key of the value.</param>
	/// <param name="load">Reads the value from the database; <see langword="null" /> when it is not stored (never cached).</param>
	/// <returns>The value, or <see langword="null" /> when none is stored.</returns>
	public async Task<TValue?> GetAsync(TKey key, Func<Task<TValue?>> load)
	{
		long removalsBefore;
		lock (_gate)
		{
			if (cache.TryGetValue(Slot(key), out TValue? cached)) return cached;
			removalsBefore = _removals;
		}

		var loaded = await load();
		if (loaded is null) return null;

		lock (_gate)
		{
			if (_removals == removalsBefore) cache.Set(Slot(key), loaded);
		}

		return loaded;
	}

	/// <summary>Drops the cached value of a key, after the database stored a change to it.</summary>
	/// <param name="key">The key of the value.</param>
	public void Remove(TKey key)
	{
		lock (_gate)
		{
			_removals++;
			cache.Remove(Slot(key));
		}
	}

	// The owner is part of the key, so caches that share one IMemoryCache never see each other's entries.
	private (CacheAside<TKey, TValue>, TKey) Slot(TKey key) => (this, key);
}