using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Caching.Memory;

namespace Basil.Infrastructure.Storage.Caching;

/// <summary>Keeps the one live instance of each stored item, by key.</summary>
/// <remarks>
///     An item stays while anyone still holds it, and is kept for <see cref="IdleTime" /> after it was last asked for
///     even when nobody does; after that it is released and the next request loads it again.
/// </remarks>
internal sealed class IdentityMap<TKey, T> : IReadOnlyDictionary<TKey, T> where TKey : notnull where T : class
{
	/// <summary>How long an item nobody holds is kept after it was last asked for.</summary>
	public static readonly TimeSpan IdleTime = TimeSpan.FromMinutes(5);

	// ponytail: dead weak references are swept every SweepEvery additions, an O(n) pass; a timer would be needed only
	// if many keys are added rarely.
	private const int SweepEvery = 1024;

	private readonly MemoryCache _recent = new(new MemoryCacheOptions());
	private readonly ConcurrentDictionary<TKey, WeakReference<T>> _instances = new();
	private readonly ConcurrentDictionary<TKey, Lazy<Task<T?>>> _loading = new();
	private readonly MemoryCacheEntryOptions _keep = new() { SlidingExpiration = IdleTime };
	private int _additions;

	/// <summary>Gets the live instance of a key, loading it once however many callers ask at the same time.</summary>
	/// <returns>The live instance, or <see langword="null" /> when the loader finds nothing (which is not kept).</returns>
	public async ValueTask<T?> GetOrAddAsync(TKey key, Func<TKey, Task<T?>> load)
	{
		if (TryGetValue(key, out var live))
			return live;

		var loading = _loading.GetOrAdd(key, static (key, load) =>
			new Lazy<Task<T?>>(() => load(key), LazyThreadSafetyMode.ExecutionAndPublication), load);
		try
		{
			var loaded = await loading.Value;
			return loaded is null ? null : GetOrAdd(key, loaded);
		}
		finally
		{
			_loading.TryRemove(new KeyValuePair<TKey, Lazy<Task<T?>>>(key, loading));
		}
	}

	/// <summary>Keeps an item unless the key already has a live instance, and returns the live one.</summary>
	public T GetOrAdd(TKey key, T item)
	{
		while (true)
		{
			if (TryGetValue(key, out var live))
				return live;

			var reference = new WeakReference<T>(item);
			if (_instances.TryAdd(key, reference) ||
			    _instances.TryGetValue(key, out var dead) && !dead.TryGetTarget(out _) &&
			    _instances.TryUpdate(key, reference, dead))
			{
				Touch(key, item);
				if (Interlocked.Increment(ref _additions) % SweepEvery == 0)
					Sweep();
				return item;
			}
		}
	}

	/// <summary>Forgets the instance of a key.</summary>
	/// <returns><see langword="true" /> when the key had one.</returns>
	public bool Remove(TKey key)
	{
		_recent.Remove(key);
		return _instances.TryRemove(key, out _);
	}

	/// <summary>Replaces the live instance of a key with <paramref name="update" /> of it.</summary>
	/// <returns><see langword="false" /> when the key has no live instance.</returns>
	public bool TryUpdate(TKey key, Func<T, T> update)
	{
		while (_instances.TryGetValue(key, out var reference) && reference.TryGetTarget(out var current))
		{
			var replacement = update(current);
			if (!_instances.TryUpdate(key, new WeakReference<T>(replacement), reference))
				continue;

			Touch(key, replacement);
			return true;
		}

		return false;
	}

	public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out T value)
	{
		if (_recent.TryGetValue(key, out value) && value is not null)
			return true;

		if (_instances.TryGetValue(key, out var reference))
		{
			if (reference.TryGetTarget(out value))
			{
				Touch(key, value);
				return true;
			}

			_instances.TryRemove(new KeyValuePair<TKey, WeakReference<T>>(key, reference));
		}

		value = null;
		return false;
	}

	public bool ContainsKey(TKey key) => TryGetValue(key, out _);

	public T this[TKey key] => TryGetValue(key, out var value)
		? value
		: throw new KeyNotFoundException($"The key '{key}' has no live instance.");

	public IEnumerable<TKey> Keys => this.Select(item => item.Key).ToArray();
	public IEnumerable<T> Values => this.Select(item => item.Value).ToArray();
	public int Count => this.Count();

	public IEnumerator<KeyValuePair<TKey, T>> GetEnumerator()
	{
		foreach (var (key, reference) in _instances)
			if (reference.TryGetTarget(out var value))
				yield return new KeyValuePair<TKey, T>(key, value);
	}

	System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

	private void Touch(TKey key, T value) => _recent.Set(key, value, _keep);

	private void Sweep()
	{
		foreach (var (key, reference) in _instances)
			if (!reference.TryGetTarget(out _))
				_instances.TryRemove(new KeyValuePair<TKey, WeakReference<T>>(key, reference));
	}
}
