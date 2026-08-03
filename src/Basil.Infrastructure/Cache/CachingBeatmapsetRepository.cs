using Basil.Application.Abstractions.Beatmaps;
using Basil.Domain.Beatmaps;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Cache;

/// <summary>
///     Read-through <see cref="IMemoryCache" /> decorator over the real
///     <see cref="IBeatmapsetRepository" />, same pattern as <see cref="CachingBeatmapRepository" /> and
///     <see cref="CachingUserRepository" />, keyed by <c>Id</c> only (a beatmapset has no md5 concept).
///     Every writes invalidates the affected entry immediately; the TTL is only a safety net.
/// </summary>
public sealed class CachingBeatmapsetRepository(
	IBeatmapsetRepository inner,
	IMemoryCache cache,
	ILogger<CachingBeatmapsetRepository> logger,
	TimeSpan? ttl = null)
	: IBeatmapsetRepository
{
	/// <summary>The entry TTL: the safety net beneath explicit invalidation.</summary>
	private readonly TimeSpan _ttl = ttl ?? TimeSpan.FromMinutes(5);

	/// <inheritdoc cref="IBeatmapsetRepository.FetchByIdAsync" />
	/// <remarks>
	///     Read-through: a hit returns immediately, a miss fetches from the underlying repository, and
	///     caches the non-null result.
	/// </remarks>
	public async Task<Beatmapset?> FetchByIdAsync(int id, CancellationToken cancellationToken = default)
	{
		var key = IdKey(id);
		if (cache.TryGetValue(key, out Beatmapset? cached))
		{
			logger.LogDebug("Cache hit {Key}", key);
			return cached;
		}

		logger.LogDebug("Cache miss {Key}", key);
		var mapset = await inner.FetchByIdAsync(id, cancellationToken);
		if (mapset is not null) cache.Set(key, mapset, _ttl);
		return mapset;
	}

	/// <inheritdoc cref="IBeatmapsetRepository.UpsertAsync" />
	/// <remarks>Invalidates the id-keyed entry after persisting.</remarks>
	public async Task<Beatmapset> UpsertAsync(Beatmapset beatmapset, CancellationToken cancellationToken = default)
	{
		var resolved = await inner.UpsertAsync(beatmapset, cancellationToken);
		cache.Remove(IdKey(resolved.Id));
		return resolved;
	}

	/// <inheritdoc cref="IBeatmapsetRepository.DeleteAsync" />
	/// <remarks>Invalidates the id-keyed entry after deleting.</remarks>
	public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
	{
		await inner.DeleteAsync(id, cancellationToken);
		cache.Remove(IdKey(id));
	}

	/// <inheritdoc cref="IBeatmapsetRepository.FetchMaxIdAsync" />
	/// <remarks>Passes straight through: not a specific-row lookup worth caching.</remarks>
	public Task<int> FetchMaxIdAsync(CancellationToken cancellationToken = default)
	{
		return inner.FetchMaxIdAsync(cancellationToken);
	}

	/// <summary>Uncached: used for the full reconciliation pass, not a hot single-row lookup.</summary>
	public Task<IReadOnlyList<int>> FetchAllIdsAsync(CancellationToken cancellationToken = default)
	{
		return inner.FetchAllIdsAsync(cancellationToken);
	}

	/// <summary>Uncached: a list-shaped call, not a single-row lookup.</summary>
	public Task<IReadOnlyList<Beatmapset>> FetchPageAsync(int offset, int limit, bool onlyWithVisibleBeatmaps,
		CancellationToken cancellationToken = default)
	{
		return inner.FetchPageAsync(offset, limit, onlyWithVisibleBeatmaps, cancellationToken);
	}

	/// <inheritdoc cref="IBeatmapsetRepository.SetFrozenAsync" />
	/// <remarks>Invalidates the id-keyed entry after updating.</remarks>
	public async Task SetFrozenAsync(int id, bool frozen, CancellationToken cancellationToken = default)
	{
		await inner.SetFrozenAsync(id, frozen, cancellationToken);
		cache.Remove(IdKey(id));
	}

	/// <inheritdoc cref="IBeatmapsetRepository.SetPrivateAsync" />
	/// <remarks>Invalidates the id-keyed entry after updating.</remarks>
	public async Task SetPrivateAsync(int id, bool isPrivate, CancellationToken cancellationToken = default)
	{
		await inner.SetPrivateAsync(id, isPrivate, cancellationToken);
		cache.Remove(IdKey(id));
	}

	/// <inheritdoc cref="IBeatmapsetRepository.SetBackgroundFileAsync" />
	/// <remarks>Invalidates the id-keyed entry after updating.</remarks>
	public async Task SetBackgroundFileAsync(int id, string? backgroundFile,
		CancellationToken cancellationToken = default)
	{
		await inner.SetBackgroundFileAsync(id, backgroundFile, cancellationToken);
		cache.Remove(IdKey(id));
	}

	/// <inheritdoc cref="IBeatmapsetRepository.SetAudioFileAsync" />
	/// <remarks>Invalidates the id-keyed entry after updating.</remarks>
	public async Task SetAudioFileAsync(int id, string? audioFile, CancellationToken cancellationToken = default)
	{
		await inner.SetAudioFileAsync(id, audioFile, cancellationToken);
		cache.Remove(IdKey(id));
	}

	/// <summary>Uncached: a live counter read, not a single-row lookup.</summary>
	public Task<int> FetchCountAsync(bool includePrivate, CancellationToken cancellationToken = default)
	{
		return inner.FetchCountAsync(includePrivate, cancellationToken);
	}

	/// <summary>Builds the id-qualified cache key for a beatmapset.</summary>
	private static string IdKey(int id)
	{
		return $"Beatmapset:Id:{id}";
	}
}