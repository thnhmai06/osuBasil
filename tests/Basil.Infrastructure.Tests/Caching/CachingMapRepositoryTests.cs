using Basil.Application.Abstractions.Beatmaps;
using Basil.Domain.Beatmaps;
using Basil.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Memory;

namespace Basil.Infrastructure.Tests.Caching;

public class CachingMapRepositoryTests
{
    private static Beatmap MakeBeatmap(int id, string md5)
    {
        var mapset = new Mapset(1000 + id, "Artist", "Title", "Creator", DateTime.UnixEpoch, DateTime.UnixEpoch);
        return new Beatmap(md5, id, mapset, "Normal", "map.osu", TimeSpan.FromMinutes(2), 500, 0, 0,
            new Difficulty(GameMode.Standard, 180, 4, 8, 8, 5, 5.0), new Dictionary<string, int>());
    }

    [Fact]
    public async Task FetchOneAsync_ById_SecondCall_DoesNotHitInner()
    {
        var beatmap = MakeBeatmap(1, new string('a', 32));
        var inner = new CountingMapRepository();
        inner.ById[1] = beatmap;
        var repo = new CachingMapRepository(inner, new MemoryCache(new MemoryCacheOptions()));

        await repo.FetchOneAsync(id: 1);
        await repo.FetchOneAsync(id: 1);

        Assert.Equal(1, inner.FetchOneCalls);
    }

    [Fact]
    public async Task FetchOneAsync_ByMd5_SecondCall_DoesNotHitInner()
    {
        var beatmap = MakeBeatmap(1, new string('a', 32));
        var inner = new CountingMapRepository();
        inner.ByMd5[beatmap.Md5] = beatmap;
        var repo = new CachingMapRepository(inner, new MemoryCache(new MemoryCacheOptions()));

        await repo.FetchOneAsync(md5: beatmap.Md5);
        await repo.FetchOneAsync(md5: beatmap.Md5);

        Assert.Equal(1, inner.FetchOneCalls);
    }

    [Fact]
    public async Task FetchOneAsync_ByFilenameAndSetId_AlwaysPassesThrough()
    {
        var inner = new CountingMapRepository();
        var repo = new CachingMapRepository(inner, new MemoryCache(new MemoryCacheOptions()));

        await repo.FetchOneAsync(filename: "a.osu", setId: 1);
        await repo.FetchOneAsync(filename: "a.osu", setId: 1);

        Assert.Equal(2, inner.FetchOneCalls);
    }

    [Fact]
    public async Task UpsertAsync_InvalidatesBothIdAndMd5Entries()
    {
        var original = MakeBeatmap(1, new string('a', 32));
        var inner = new CountingMapRepository();
        inner.ById[1] = original;
        inner.ByMd5[original.Md5] = original;
        var repo = new CachingMapRepository(inner, new MemoryCache(new MemoryCacheOptions()));

        await repo.FetchOneAsync(id: 1);
        await repo.FetchOneAsync(md5: original.Md5);
        Assert.Equal(2, inner.FetchOneCalls);

        var updated = original with { Version = "Changed" };
        inner.UpsertResult = updated;
        await repo.UpsertAsync(updated);
        inner.ById[1] = updated;
        inner.ByMd5[original.Md5] = updated;

        await repo.FetchOneAsync(id: 1);
        await repo.FetchOneAsync(md5: original.Md5);

        Assert.Equal(4, inner.FetchOneCalls);
    }

    private sealed class CountingMapRepository : IMapRepository
    {
        public int FetchOneCalls { get; private set; }
        public Dictionary<int, Beatmap> ById { get; } = new();
        public Dictionary<string, Beatmap> ByMd5 { get; } = new();
        public Beatmap? UpsertResult { get; set; }

        public Task<Beatmap?> FetchOneAsync(int? id = null, string? md5 = null, string? filename = null,
            int? setId = null, bool includePrivate = false, CancellationToken cancellationToken = default)
        {
            FetchOneCalls++;
            if (id is not null) return Task.FromResult(ById.GetValueOrDefault(id.Value));
            if (md5 is not null) return Task.FromResult(ByMd5.GetValueOrDefault(md5));
            return Task.FromResult<Beatmap?>(null);
        }

        public Task<Beatmap> UpsertAsync(Beatmap beatmap, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(UpsertResult ?? beatmap);
        }

        public Task DeleteByMd5Async(string md5, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<IReadOnlyList<Beatmap>>> SearchAsync(string? query, GameMode? mode, int offset,
            int amount, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<IReadOnlyList<Beatmap>>>([]);
        }

        public Task IncrementPlayCountsAsync(int mapId, bool passed, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<int> FetchMaxIdAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }

        public Task UpdateDiffAsync(int id, double diff, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Beatmap>> FetchAllBySetIdAsync(int setId, bool includePrivate = false,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Beatmap>>([]);
        }
    }
}
