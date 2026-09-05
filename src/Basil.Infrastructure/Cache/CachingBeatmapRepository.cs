using Basil.Application.Abstractions.Beatmaps;
using Basil.Domain.Beatmaps;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Cache;

/// <summary>
///     Read-through caching decorator over the real <see cref="IBeatmapRepository" />: eliminates
///     the N+1 that embedding a full <see cref="Beatmap" /> reference into every response that
///     carries one would otherwise cause. Only the two single-row lookup shapes (by <c>Id</c>, by
///     <c>Md5</c>) are cached; a filename/setId-qualified call always passes through. Every write
///     invalidates the affected entry immediately; the TTL is only a safety net, not a substitute
///     for it.
/// </summary>
public sealed class CachingBeatmapRepository(
	IBeatmapRepository inner,
	IMemoryCache cache,
	ILogger<CachingBeatmapRepository> logger,
	TimeSpan? ttl = null)
	: IBeatmapRepository
{
	/// <summary>The entry TTL: the safety net beneath explicit invalidation.</summary>
	private readonly TimeSpan _ttl = ttl ?? TimeSpan.FromMinutes(5);

	/// <inheritdoc cref="IBeatmapRepository.FetchOneAsync" />
	/// <remarks>
	///     Lookups qualified by id alone, or by md5 alone, are served from the cache; any other
	///     combination passes straight through to the underlying repository.
	/// </remarks>
	public Task<Beatmap?> FetchOneAsync(int? id = null, string? md5 = null, string? filename = null,
		int? setId = null, bool includePrivate = false, CancellationToken cancellationToken = default)
	{
		if (id is not null && md5 is null && filename is null && setId is null)
			return FetchCachedAsync(IdKey(id.Value, includePrivate),
				() => inner.FetchOneAsync(id, includePrivate: includePrivate, cancellationToken: cancellationToken));

		if (md5 is not null && id is null && filename is null && setId is null)
			return FetchCachedAsync(Md5Key(md5, includePrivate),
				() => inner.FetchOneAsync(md5: md5, includePrivate: includePrivate,
					cancellationToken: cancellationToken));

		return inner.FetchOneAsync(id, md5, filename, setId, includePrivate, cancellationToken);
	}

	/// <inheritdoc cref="IBeatmapRepository.UpsertAsync" />
	/// <remarks>
	///     Invalidates both the id- and md5-keyed entries for the resolved beatmap after persisting.
	/// </remarks>
	public async Task<Beatmap> UpsertAsync(Beatmap beatmap, CancellationToken cancellationToken = default)
	{
		var resolved = await inner.UpsertAsync(beatmap, cancellationToken);
		Invalidate(resolved);
		return resolved;
	}

	/// <inheritdoc cref="IBeatmapRepository.DeleteByMd5Async" />
	/// <remarks>
	///     Reads the cached row (if any) so its id-keyed entry can be invalidated alongside the md5
	///     entry.
	/// </remarks>
	public async Task DeleteByMd5Async(string md5, CancellationToken cancellationToken = default)
	{
		if (!cache.TryGetValue(Md5Key(md5, true), out Beatmap? cached))
			cache.TryGetValue(Md5Key(md5, false), out cached);
		await inner.DeleteByMd5Async(md5, cancellationToken);
		cache.Remove(Md5Key(md5, true));
		cache.Remove(Md5Key(md5, false));
		if (cached is not null)
		{
			cache.Remove(IdKey(cached.Id, true));
			cache.Remove(IdKey(cached.Id, false));
		}
	}

	/// <summary>Uncached: a discovery/listing surface, not a specific-row lookup.</summary>
	public Task<IReadOnlyList<IReadOnlyList<Beatmap>>> SearchAsync(BeatmapsetSearchFilters filters, GameMode? mode,
		int offset, int amount, CancellationToken cancellationToken = default)
	{
		return inner.SearchAsync(filters, mode, offset, amount, cancellationToken);
	}

	/// <summary>Uncached: paired with the uncached <see cref="SearchAsync" />.</summary>
	public Task<int> SearchCountAsync(BeatmapsetSearchFilters filters, GameMode? mode,
		CancellationToken cancellationToken = default)
	{
		return inner.SearchCountAsync(filters, mode, cancellationToken);
	}

	/// <inheritdoc cref="IBeatmapRepository.FetchMaxIdAsync" />
	/// <remarks>Passes straight through: not a specific-row lookup worth caching.</remarks>
	public Task<int> FetchMaxIdAsync(CancellationToken cancellationToken = default)
	{
		return inner.FetchMaxIdAsync(cancellationToken);
	}

	/// <inheritdoc cref="IBeatmapRepository.UpdateDiffAsync" />
	/// <remarks>Invalidates the id-keyed entry after updating.</remarks>
	public async Task UpdateDiffAsync(int id, double diff, CancellationToken cancellationToken = default)
	{
		await inner.UpdateDiffAsync(id, diff, cancellationToken);
		cache.Remove(IdKey(id, true));
		cache.Remove(IdKey(id, false));
	}

	/// <summary>Uncached: a list-shaped call (every difficulty in a set), not a single-row lookup.</summary>
	public Task<IReadOnlyList<Beatmap>> FetchAllBySetIdAsync(int setId, bool includePrivate = false,
		CancellationToken cancellationToken = default)
	{
		return inner.FetchAllBySetIdAsync(setId, includePrivate, cancellationToken);
	}

	/// <summary>Uncached: a batched, multi-set call, not a single-row lookup.</summary>
	public Task<IReadOnlyDictionary<int, int>> FetchCountsBySetIdsAsync(IReadOnlyCollection<int> setIds,
		bool includePrivate = false, CancellationToken cancellationToken = default)
	{
		return inner.FetchCountsBySetIdsAsync(setIds, includePrivate, cancellationToken);
	}

	/// <summary>
	///     Reads a keyed entry, falling back to the given fetch and caching its non-null result.
	/// </summary>
	/// <param name="key">The cache key to read and populate.</param>
	/// <param name="fetch">The lookup to run on a cache miss.</param>
	/// <returns>The cached or freshly fetched beatmap, or <see langword="null" /> when absent.</returns>
	private async Task<Beatmap?> FetchCachedAsync(string key, Func<Task<Beatmap?>> fetch)
	{
		if (cache.TryGetValue(key, out Beatmap? cached))
		{
			logger.LogDebug("Cache hit {Key}", key);
			return cached;
		}

		logger.LogDebug("Cache miss {Key}", key);
		var beatmap = await fetch();
		if (beatmap is not null)
			cache.Set(key, beatmap, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = _ttl, Size = 1 });
		return beatmap;
	}

	/// <summary>Removes both the id- and md5-keyed entries for a beatmap, in both privacy variants.</summary>
	private void Invalidate(Beatmap beatmap)
	{
		cache.Remove(IdKey(beatmap.Id, true));
		cache.Remove(IdKey(beatmap.Id, false));
		cache.Remove(Md5Key(beatmap.Md5, true));
		cache.Remove(Md5Key(beatmap.Md5, false));
	}

	/// <summary>
	///     Builds the id-qualified cache key for a beatmap, keyed separately per
	///     <paramref name="includePrivate" /> so a private-inclusive lookup can never be served back to
	///     a caller that asked to exclude private beatmaps.
	/// </summary>
	private static string IdKey(int id, bool includePrivate)
	{
		return $"Beatmap:Id:{id}:{includePrivate}";
	}

	/// <summary>
	///     Builds the md5-qualified cache key for a beatmap, keyed separately per
	///     <paramref name="includePrivate" /> so a private-inclusive lookup can never be served back to
	///     a caller that asked to exclude private beatmaps.
	/// </summary>
	private static string Md5Key(string md5, bool includePrivate)
	{
		return $"Beatmap:Md5:{md5}:{includePrivate}";
	}
}