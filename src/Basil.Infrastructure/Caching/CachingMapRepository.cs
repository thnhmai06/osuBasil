using Basil.Application.Abstractions.Beatmaps;
using Basil.Domain.Beatmaps;
using Microsoft.Extensions.Caching.Memory;

namespace Basil.Infrastructure.Caching;

/// <summary>
///     Read-through <see cref="IMemoryCache" /> decorator over the real <see cref="IMapRepository" />
///     — eliminates the N+1 that embedding a full <see cref="Beatmap" /> reference into every
///     multiplayer/score/TRT response would otherwise cause. Only the two single-row lookup shapes
///     actually used by API routes/report building (by <c>Id</c>, by <c>Md5</c>) are cached; a
///     filename/setId-qualified call always passes through — Simplicity First, those aren't the hot
///     paths this decorator exists for. Every write invalidates the affected entry immediately; the
///     TTL is only a safety net, not a substitute for it.
/// </summary>
public sealed class CachingMapRepository(IMapRepository inner, IMemoryCache cache, TimeSpan? ttl = null)
    : IMapRepository
{
    private readonly TimeSpan _ttl = ttl ?? TimeSpan.FromMinutes(5);

    public Task<Beatmap?> FetchOneAsync(int? id = null, string? md5 = null, string? filename = null,
        int? setId = null, bool includePrivate = false, CancellationToken cancellationToken = default)
    {
        if (id is not null && md5 is null && filename is null && setId is null)
            return FetchCachedAsync(IdKey(id.Value),
                () => inner.FetchOneAsync(id: id, includePrivate: includePrivate, cancellationToken: cancellationToken));

        if (md5 is not null && id is null && filename is null && setId is null)
            return FetchCachedAsync(Md5Key(md5),
                () => inner.FetchOneAsync(md5: md5, includePrivate: includePrivate, cancellationToken: cancellationToken));

        return inner.FetchOneAsync(id, md5, filename, setId, includePrivate, cancellationToken);
    }

    private async Task<Beatmap?> FetchCachedAsync(string key, Func<Task<Beatmap?>> fetch)
    {
        if (cache.TryGetValue(key, out Beatmap? cached)) return cached;

        var beatmap = await fetch();
        if (beatmap is not null) cache.Set(key, beatmap, _ttl);
        return beatmap;
    }

    public async Task<Beatmap> UpsertAsync(Beatmap beatmap, CancellationToken cancellationToken = default)
    {
        var resolved = await inner.UpsertAsync(beatmap, cancellationToken);
        Invalidate(resolved);
        return resolved;
    }

    public async Task DeleteByMd5Async(string md5, CancellationToken cancellationToken = default)
    {
        cache.TryGetValue(Md5Key(md5), out Beatmap? cached);
        await inner.DeleteByMd5Async(md5, cancellationToken);
        cache.Remove(Md5Key(md5));
        if (cached is not null) cache.Remove(IdKey(cached.Id));
    }

    /// <summary>Uncached — a discovery/listing surface, not a specific-row lookup.</summary>
    public Task<IReadOnlyList<IReadOnlyList<Beatmap>>> SearchAsync(string? query, GameMode? mode, int offset,
        int amount, CancellationToken cancellationToken = default)
    {
        return inner.SearchAsync(query, mode, offset, amount, cancellationToken);
    }

    public async Task IncrementPlayCountsAsync(int mapId, bool passed, CancellationToken cancellationToken = default)
    {
        await inner.IncrementPlayCountsAsync(mapId, passed, cancellationToken);
        // Fires on every score submission — deliberately not looking the row up first just to also
        // invalidate its Md5-keyed entry (that entry's Plays/Passes goes stale for at most the TTL,
        // an acceptable trade-off for not adding a DB round-trip to this hot path).
        cache.Remove(IdKey(mapId));
    }

    public Task<int> FetchMaxIdAsync(CancellationToken cancellationToken = default)
    {
        return inner.FetchMaxIdAsync(cancellationToken);
    }

    public async Task UpdateDiffAsync(int id, double diff, CancellationToken cancellationToken = default)
    {
        await inner.UpdateDiffAsync(id, diff, cancellationToken);
        cache.Remove(IdKey(id));
    }

    /// <summary>Uncached — a list-shaped call (every difficulty in a set), not a single-row lookup.</summary>
    public Task<IReadOnlyList<Beatmap>> FetchAllBySetIdAsync(int setId, bool includePrivate = false,
        CancellationToken cancellationToken = default)
    {
        return inner.FetchAllBySetIdAsync(setId, includePrivate, cancellationToken);
    }

    private void Invalidate(Beatmap beatmap)
    {
        cache.Remove(IdKey(beatmap.Id));
        cache.Remove(Md5Key(beatmap.Md5));
    }

    private static string IdKey(int id)
    {
        return $"Beatmap:Id:{id}";
    }

    private static string Md5Key(string md5)
    {
        return $"Beatmap:Md5:{md5}";
    }
}
