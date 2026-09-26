namespace Basil.Application.Contracts.Caching;

/// <summary>
///     An explicit, lazily-populated in-memory cache in front of a kho (repository, registry, or
///     storage) implementation.
/// </summary>
/// <remarks>
///     Only the implementation of a kho may use this: it is not a kho of its own, and no other
///     Application code should hold or bypass one. Concurrent calls for the same key that has not
///     been loaded yet share a single load. An entry is released after five minutes without being
///     read or written.
/// </remarks>
/// <typeparam name="TKey">The type that uniquely identifies a cached value.</typeparam>
/// <typeparam name="TValue">The type of value cached.</typeparam>
public interface ICache<in TKey, TValue> where TKey : notnull
{
	/// <summary>Gets the cached value for <paramref name="key" />, loading and caching it if absent.</summary>
	/// <param name="key">The key to look up.</param>
	/// <param name="cancellationToken">A token that cancels the load.</param>
	/// <returns>The cached or freshly loaded value.</returns>
	ValueTask<TValue> GetOrLoadAsync(TKey key, CancellationToken cancellationToken = default);

	/// <summary>Sets or replaces the cached value for <paramref name="key" />.</summary>
	/// <param name="key">The key to set.</param>
	/// <param name="value">The value to cache.</param>
	void Set(TKey key, TValue value);

	/// <summary>Removes any cached value for <paramref name="key" />, so the next read loads it again.</summary>
	/// <param name="key">The key to invalidate.</param>
	void Invalidate(TKey key);
}