using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Basil.Infrastructure.Storage.Caching;

/// <summary>Keeps the one live instance of each stored item, by key.</summary>
internal sealed class IdentityMap<TKey, T> : IReadOnlyDictionary<TKey, T> where TKey : notnull where T : class
{
	private readonly ConcurrentDictionary<TKey, Lazy<Task<T?>>> _items = new();

	public async ValueTask<T?> GetOrAddAsync(TKey key, Func<TKey, Task<T?>> load)
	{
		var entry = _items.GetOrAdd(key, static (key, load) =>
			new Lazy<Task<T?>>(() => load(key), LazyThreadSafetyMode.ExecutionAndPublication), load);
		try
		{
			var value = await entry.Value;
			if (value is null)
				_items.TryRemove(new KeyValuePair<TKey, Lazy<Task<T?>>>(key, entry));

			return value;
		}
		catch
		{
			_items.TryRemove(new KeyValuePair<TKey, Lazy<Task<T?>>>(key, entry));
			throw;
		}
	}

	public T GetOrAdd(TKey key, T item)
	{
		while (true)
		{
			var candidate = new Lazy<Task<T?>>(() => Task.FromResult<T?>(item),
				LazyThreadSafetyMode.ExecutionAndPublication);
			var entry = _items.GetOrAdd(key, candidate);
			if (ReferenceEquals(candidate, entry))
				return item;

			T? existing;
			try
			{
				// ponytail: waits synchronously when a load of the same key is still running, which is rare and
				// short; an async Track would be needed only if that wait shows up under load.
				existing = entry.Value.GetAwaiter().GetResult();
			}
			catch
			{
				_items.TryRemove(new KeyValuePair<TKey, Lazy<Task<T?>>>(key, entry));
				continue;
			}

			if (existing is not null)
				return existing;

			_items.TryRemove(new KeyValuePair<TKey, Lazy<Task<T?>>>(key, entry));
		}
	}

	public bool Remove(TKey key) => _items.TryRemove(key, out _);

	public bool TryUpdate(TKey key, Func<T, T> update)
	{
		while (_items.TryGetValue(key, out var entry))
		{
			if (!entry.IsValueCreated || !entry.Value.IsCompletedSuccessfully || entry.Value.Result is not { } current)
				return false;

			var replacement = new Lazy<Task<T?>>(() => Task.FromResult<T?>(update(current)),
				LazyThreadSafetyMode.ExecutionAndPublication);
			if (_items.TryUpdate(key, replacement, entry))
				return true;
		}

		return false;
	}

	public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out T value)
	{
		if (_items.TryGetValue(key, out var entry) && entry.IsValueCreated &&
		    entry.Value.IsCompletedSuccessfully && entry.Value.Result is { } loaded)
		{
			value = loaded;
			return true;
		}

		value = null;
		return false;
	}

	public bool ContainsKey(TKey key) => TryGetValue(key, out _);

	public T this[TKey key] => TryGetValue(key, out var value)
		? value
		: throw new KeyNotFoundException($"The key '{key}' is not loaded.");

	public IEnumerable<TKey> Keys => this.Select(item => item.Key).ToArray();
	public IEnumerable<T> Values => this.Select(item => item.Value).ToArray();
	public int Count => this.Count();

	public IEnumerator<KeyValuePair<TKey, T>> GetEnumerator()
	{
		foreach (var (key, entry) in _items)
		{
			if (entry.IsValueCreated && entry.Value.IsCompletedSuccessfully && entry.Value.Result is { } value)
				yield return new KeyValuePair<TKey, T>(key, value);
		}
	}

	System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
