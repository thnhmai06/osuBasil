using Basil.Domain.Beatmaps;
using Basil.Infrastructure.Persistence.Repositories;

namespace Basil.Infrastructure.Tests.Persistence;

public class SqliteMapsetRepositoryTests(SqliteFixture fixture) : IClassFixture<SqliteFixture>
{
    private readonly SqliteMapsetRepository _mapsetRepository = new(fixture.ConnectionString);
    private readonly SqliteMapRepository _mapRepository = new(fixture.ConnectionString);

    private static Mapset MakeMapset(int id)
    {
        return new Mapset(id, "Camellia", "Exit This Earth's Atomosphere", "cmyui",
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task UpsertThenFetchById_ReturnsMapset()
    {
        var mapset = MakeMapset(9001);

        await _mapsetRepository.UpsertAsync(mapset);
        var fetched = await _mapsetRepository.FetchByIdAsync(mapset.Id);

        Assert.Equal(mapset, fetched);
    }

    [Fact]
    public async Task FetchById_Unknown_ReturnsNull()
    {
        Assert.Null(await _mapsetRepository.FetchByIdAsync(-1));
    }

    [Fact]
    public async Task Upsert_ExistingId_ReplacesRow()
    {
        var mapset = MakeMapset(9002);
        await _mapsetRepository.UpsertAsync(mapset);

        var updated = mapset with { Artist = "Updated Artist" };
        await _mapsetRepository.UpsertAsync(updated);

        var fetched = await _mapsetRepository.FetchByIdAsync(mapset.Id);
        Assert.Equal("Updated Artist", fetched!.Artist);
    }

    [Fact]
    public async Task DeleteAsync_CascadesToBeatmaps()
    {
        var mapset = MakeMapset(9003);
        await _mapsetRepository.UpsertAsync(mapset);

        var beatmap = new Beatmap(new string('z', 32), 9003001, mapset, "Hyper", "z.osu",
            TimeSpan.FromSeconds(120), 500, false, 0, 0,
            new Difficulty(GameMode.Standard, 180.0, 4.0, 9.0, 8.0, 5.0, 6.5));
        await _mapRepository.UpsertAsync(beatmap);

        await _mapsetRepository.DeleteAsync(mapset.Id);

        Assert.Null(await _mapsetRepository.FetchByIdAsync(mapset.Id));
        Assert.Null(await _mapRepository.FetchOneAsync(beatmap.Id, includePrivate: true));
    }

    [Fact]
    public async Task FetchMaxIdAsync_ReturnsHighestId()
    {
        await _mapsetRepository.UpsertAsync(MakeMapset(9010));
        await _mapsetRepository.UpsertAsync(MakeMapset(9011));

        var maxId = await _mapsetRepository.FetchMaxIdAsync();

        Assert.True(maxId >= 9011);
    }

    [Fact]
    public async Task FetchAllIdsAsync_IncludesUpsertedIds()
    {
        await _mapsetRepository.UpsertAsync(MakeMapset(9020));
        await _mapsetRepository.UpsertAsync(MakeMapset(9021));

        var ids = await _mapsetRepository.FetchAllIdsAsync();

        Assert.Contains(9020, ids);
        Assert.Contains(9021, ids);
    }

    [Fact]
    public async Task SetFrozenAsync_TogglesIsFrozen()
    {
        var mapset = MakeMapset(9030);
        await _mapsetRepository.UpsertAsync(mapset);

        await _mapsetRepository.SetFrozenAsync(mapset.Id, true);
        Assert.True((await _mapsetRepository.FetchByIdAsync(mapset.Id))!.IsFrozen);

        await _mapsetRepository.SetFrozenAsync(mapset.Id, false);
        Assert.False((await _mapsetRepository.FetchByIdAsync(mapset.Id))!.IsFrozen);
    }

    [Fact]
    public async Task Upsert_ExistingFrozenMapset_ReingestionDoesNotClearFreeze()
    {
        var mapset = MakeMapset(9031);
        await _mapsetRepository.UpsertAsync(mapset);
        await _mapsetRepository.SetFrozenAsync(mapset.Id, true);

        var reingested = await _mapsetRepository.UpsertAsync(mapset with { Artist = "Re-ingested Artist" });

        Assert.True(reingested.IsFrozen);
        Assert.True((await _mapsetRepository.FetchByIdAsync(mapset.Id))!.IsFrozen);
    }
}
