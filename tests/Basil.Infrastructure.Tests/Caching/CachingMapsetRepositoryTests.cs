using Basil.Application.Abstractions.Beatmaps;
using Basil.Domain.Beatmaps;
using Basil.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Memory;

namespace Basil.Infrastructure.Tests.Caching;

public class CachingMapsetRepositoryTests
{
    private static Mapset MakeMapset(int id)
    {
        return new Mapset(id, "Artist", "Title", "Creator", DateTime.UnixEpoch, DateTime.UnixEpoch);
    }

    [Fact]
    public async Task FetchByIdAsync_SecondCall_DoesNotHitInner()
    {
        var inner = new CountingMapsetRepository();
        inner.ById[1] = MakeMapset(1);
        var repo = new CachingMapsetRepository(inner, new MemoryCache(new MemoryCacheOptions()));

        await repo.FetchByIdAsync(1);
        await repo.FetchByIdAsync(1);

        Assert.Equal(1, inner.FetchByIdCalls);
    }

    [Fact]
    public async Task SetFrozenAsync_InvalidatesCachedEntry()
    {
        var inner = new CountingMapsetRepository();
        inner.ById[1] = MakeMapset(1);
        var repo = new CachingMapsetRepository(inner, new MemoryCache(new MemoryCacheOptions()));

        await repo.FetchByIdAsync(1);
        await repo.SetFrozenAsync(1, true);
        await repo.FetchByIdAsync(1);

        Assert.Equal(2, inner.FetchByIdCalls);
    }

    [Fact]
    public async Task SetPrivateAsync_InvalidatesCachedEntry()
    {
        var inner = new CountingMapsetRepository();
        inner.ById[1] = MakeMapset(1);
        var repo = new CachingMapsetRepository(inner, new MemoryCache(new MemoryCacheOptions()));

        await repo.FetchByIdAsync(1);
        await repo.SetPrivateAsync(1, true);
        await repo.FetchByIdAsync(1);

        Assert.Equal(2, inner.FetchByIdCalls);
    }

    [Fact]
    public async Task UpsertAsync_InvalidatesCachedEntry()
    {
        var inner = new CountingMapsetRepository();
        var original = MakeMapset(1);
        inner.ById[1] = original;
        var repo = new CachingMapsetRepository(inner, new MemoryCache(new MemoryCacheOptions()));

        await repo.FetchByIdAsync(1);
        var updated = original with { Artist = "New Artist" };
        inner.UpsertResult = updated;
        await repo.UpsertAsync(updated);
        inner.ById[1] = updated;
        await repo.FetchByIdAsync(1);

        Assert.Equal(2, inner.FetchByIdCalls);
    }

    private sealed class CountingMapsetRepository : IMapsetRepository
    {
        public int FetchByIdCalls { get; private set; }
        public Dictionary<int, Mapset> ById { get; } = new();
        public Mapset? UpsertResult { get; set; }

        public Task<Mapset?> FetchByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            FetchByIdCalls++;
            return Task.FromResult(ById.GetValueOrDefault(id));
        }

        public Task<Mapset> UpsertAsync(Mapset mapset, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(UpsertResult ?? mapset);
        }

        public Task DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<int> FetchMaxIdAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }

        public Task<IReadOnlyList<int>> FetchAllIdsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<int>>([]);
        }

        public Task<IReadOnlyList<Mapset>> FetchPageAsync(int offset, int limit, bool onlyWithVisibleBeatmaps,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Mapset>>([]);
        }

        public Task SetFrozenAsync(int id, bool frozen, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task SetPrivateAsync(int id, bool isPrivate, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<int> FetchCountAsync(bool includePrivate, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }
    }
}
