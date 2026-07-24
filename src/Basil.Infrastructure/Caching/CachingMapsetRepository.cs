using Basil.Application.Abstractions.Beatmaps;
using Basil.Domain.Beatmaps;
using Microsoft.Extensions.Caching.Memory;

namespace Basil.Infrastructure.Caching;

/// <summary>
///     Read-through <see cref="IMemoryCache" /> decorator over the real
///     <see cref="IMapsetRepository" /> — same pattern as <see cref="CachingMapRepository" />/
///     <see cref="CachingUserRepository" />, keyed by <c>Id</c> only (a mapset has no md5 concept).
///     Every write invalidates the affected entry immediately; the TTL is only a safety net.
/// </summary>
public sealed class CachingMapsetRepository(IMapsetRepository inner, IMemoryCache cache, TimeSpan? ttl = null)
    : IMapsetRepository
{
    private readonly TimeSpan _ttl = ttl ?? TimeSpan.FromMinutes(5);

    public async Task<Mapset?> FetchByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var key = IdKey(id);
        if (cache.TryGetValue(key, out Mapset? cached)) return cached;

        var mapset = await inner.FetchByIdAsync(id, cancellationToken);
        if (mapset is not null) cache.Set(key, mapset, _ttl);
        return mapset;
    }

    public async Task<Mapset> UpsertAsync(Mapset mapset, CancellationToken cancellationToken = default)
    {
        var resolved = await inner.UpsertAsync(mapset, cancellationToken);
        cache.Remove(IdKey(resolved.Id));
        return resolved;
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await inner.DeleteAsync(id, cancellationToken);
        cache.Remove(IdKey(id));
    }

    public Task<int> FetchMaxIdAsync(CancellationToken cancellationToken = default)
    {
        return inner.FetchMaxIdAsync(cancellationToken);
    }

    /// <summary>Uncached — used for the full reconciliation pass, not a hot single-row lookup.</summary>
    public Task<IReadOnlyList<int>> FetchAllIdsAsync(CancellationToken cancellationToken = default)
    {
        return inner.FetchAllIdsAsync(cancellationToken);
    }

    /// <summary>Uncached — the `GET /beatmapsets` list route, not a single-row lookup.</summary>
    public Task<IReadOnlyList<Mapset>> FetchPageAsync(int offset, int limit, bool onlyWithVisibleBeatmaps,
        CancellationToken cancellationToken = default)
    {
        return inner.FetchPageAsync(offset, limit, onlyWithVisibleBeatmaps, cancellationToken);
    }

    public async Task SetFrozenAsync(int id, bool frozen, CancellationToken cancellationToken = default)
    {
        await inner.SetFrozenAsync(id, frozen, cancellationToken);
        cache.Remove(IdKey(id));
    }

    public async Task SetPrivateAsync(int id, bool isPrivate, CancellationToken cancellationToken = default)
    {
        await inner.SetPrivateAsync(id, isPrivate, cancellationToken);
        cache.Remove(IdKey(id));
    }

    /// <summary>Uncached — a live counter read, not a single-row lookup.</summary>
    public Task<int> FetchCountAsync(bool includePrivate, CancellationToken cancellationToken = default)
    {
        return inner.FetchCountAsync(includePrivate, cancellationToken);
    }

    private static string IdKey(int id)
    {
        return $"Mapset:Id:{id}";
    }
}
