using Microsoft.Extensions.Caching.Memory;

namespace Basil.Infrastructure.Shared.Registry;

public sealed class CacheAside<TKey, TValue>(IMemoryCache cache) where TKey : notnull where TValue : class
{
	private readonly Lock _gate = new();
	private long _removals;

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

	public void Remove(TKey key)
	{
		lock (_gate)
		{
			_removals++;
			cache.Remove(Slot(key));
		}
	}

	// The owner is part of the key, so caches that share one IMemoryCache never see each other's entries.
	private (CacheAside<TKey, TValue>, TKey) Slot(TKey key)
	{
		return (this, key);
	}
}