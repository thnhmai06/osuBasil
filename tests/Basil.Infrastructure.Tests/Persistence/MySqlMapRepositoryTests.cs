using Basil.Domain.Beatmaps;
using Basil.Infrastructure.Persistence.Repositories;

namespace Basil.Infrastructure.Tests.Persistence;

/// <summary>
///     Ported from app/repositories/maps.py, scoped to what beatmap resolution (Phase 5) needs:
///     lookup by id/md5/filename and upsert. `server` is hardcoded to "osu!" everywhere.
/// </summary>
public class MySqlMapRepositoryTests(MySqlFixture fixture) : IClassFixture<MySqlFixture>
{
    private readonly MySqlMapRepository _repository = new(fixture.ConnectionString);

    private static Beatmap MakeBeatmap(int id, string md5, RankedStatus status = RankedStatus.Ranked)
    {
        return new Beatmap(
            md5,
            id,
            1000 + id,
            "Camellia",
            "Exit This Earth's Atomosphere",
            "Hyper",
            "cmyui",
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            120,
            500,
            status,
            false,
            0,
            0,
            GameMode.VanillaOsu,
            180.0,
            4.0,
            8.0,
            9.0,
            5.0,
            6.5,
            $"Camellia - Exit This Earth's Atomosphere (cmyui) [Hyper] {id}.osu");
    }

    [Fact]
    public async Task UpsertThenFetchByMd5_ReturnsBeatmap()
    {
        var bmap = MakeBeatmap(101, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        await _repository.UpsertAsync(bmap);
        var fetched = await _repository.FetchOneAsync(md5: bmap.Md5);

        Assert.NotNull(fetched);
        Assert.Equal(bmap, fetched);
    }

    [Fact]
    public async Task FetchById_ReturnsBeatmap()
    {
        var bmap = MakeBeatmap(102, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        await _repository.UpsertAsync(bmap);

        var fetched = await _repository.FetchOneAsync(bmap.Id);

        Assert.NotNull(fetched);
        Assert.Equal(bmap.Md5, fetched!.Md5);
    }

    [Fact]
    public async Task FetchByFilename_ReturnsBeatmap()
    {
        var bmap = MakeBeatmap(103, new string('c', 32));
        await _repository.UpsertAsync(bmap);

        var fetched = await _repository.FetchOneAsync(filename: bmap.Filename);

        Assert.NotNull(fetched);
        Assert.Equal(bmap.Id, fetched!.Id);
    }

    [Fact]
    public async Task FetchOne_Unknown_ReturnsNull()
    {
        Assert.Null(await _repository.FetchOneAsync(md5: new string('0', 32)));
    }

    [Fact]
    public async Task Upsert_ExistingId_ReplacesRow()
    {
        var bmap = MakeBeatmap(104, "dddddddddddddddddddddddddddddddd");
        await _repository.UpsertAsync(bmap);

        var updated = bmap with { Status = RankedStatus.Loved, Plays = 42 };
        await _repository.UpsertAsync(updated);

        var fetched = await _repository.FetchOneAsync(bmap.Id);
        Assert.Equal(RankedStatus.Loved, fetched!.Status);
        Assert.Equal(42, fetched.Plays);
    }

    [Fact]
    public async Task DeleteByMd5_RemovesRow()
    {
        var bmap = MakeBeatmap(105, "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee");
        await _repository.UpsertAsync(bmap);

        await _repository.DeleteByMd5Async(bmap.Md5);

        Assert.Null(await _repository.FetchOneAsync(bmap.Id));
    }

    [Fact]
    public async Task FetchOne_NoParameters_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _repository.FetchOneAsync());
    }

    [Fact]
    public async Task FetchOne_BySetId_ReturnsAnyMapInThatSet()
    {
        var bmap = MakeBeatmap(106, "ffffffffffffffffffffffffffffffff");
        await _repository.UpsertAsync(bmap);

        var fetched = await _repository.FetchOneAsync(setId: bmap.SetId);

        Assert.NotNull(fetched);
        Assert.Equal(bmap.SetId, fetched!.SetId);
    }

    private static Beatmap MakeBeatmap(int id, int setId, string md5, string artist, double diff,
        GameMode mode = GameMode.VanillaOsu, RankedStatus status = RankedStatus.Ranked)
    {
        return new Beatmap(
            md5, id, setId, artist, "Title", $"Diff{id}", "cmyui",
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), 120, 500,
            status, false, 0, 0, mode, 180.0, 4.0, 8.0, 9.0, 5.0,
            diff, $"{artist} - Title (cmyui) [Diff{id}].osu");
    }

    [Fact]
    public async Task SearchAsync_GroupsBySetId_NewestFirst_DiffsAscendingWithinSet()
    {
        var setA = 5001;
        var setB = 5002; // higher set_id -> newer -> should come first
        await _repository.UpsertAsync(MakeBeatmap(201, setA, new string('f', 32), "Zeta Artist", 5.0));
        await _repository.UpsertAsync(MakeBeatmap(202, setA, new string('g', 32), "Zeta Artist", 2.0));
        await _repository.UpsertAsync(MakeBeatmap(203, setB, new string('h', 32), "Alpha Artist", 3.0));

        var results = await _repository.SearchAsync(null, null, null, 0, 100);
        var relevant = results.Where(set => set[0].SetId is 5001 or 5002).ToList();

        Assert.Equal(5002, relevant[0][0].SetId);
        Assert.Equal(5001, relevant[1][0].SetId);
        Assert.Equal(2, relevant[1].Count);
        Assert.True(relevant[1][0].Diff < relevant[1][1].Diff);
    }

    [Fact]
    public async Task SearchAsync_FiltersByQueryText_MatchesArtistTitleOrCreator()
    {
        var setId = 5010;
        await _repository.UpsertAsync(MakeBeatmap(210, setId, new string('i', 32), "UniqueArtistName210", 1.0));

        var results = await _repository.SearchAsync("UniqueArtistName210", null, null, 0, 100);

        Assert.Single(results);
        Assert.Equal(setId, results[0][0].SetId);
    }

    [Fact]
    public async Task SearchAsync_FiltersByMode()
    {
        var setId = 5020;
        await _repository.UpsertAsync(MakeBeatmap(220, setId, new string('j', 32), "ModeFilterArtist220", 1.0,
            GameMode.VanillaTaiko));

        var matching = await _repository.SearchAsync("ModeFilterArtist220", GameMode.VanillaTaiko, null, 0, 100);
        var nonMatching = await _repository.SearchAsync("ModeFilterArtist220", GameMode.VanillaCatch, null, 0, 100);

        Assert.Single(matching);
        Assert.Empty(nonMatching);
    }

    [Fact]
    public async Task SearchAsync_FiltersByStatus()
    {
        var setId = 5030;
        await _repository.UpsertAsync(MakeBeatmap(230, setId, new string('k', 32), "StatusFilterArtist230", 1.0,
            status: RankedStatus.Loved));

        var matching = await _repository.SearchAsync("StatusFilterArtist230", null, RankedStatus.Loved, 0, 100);
        var nonMatching = await _repository.SearchAsync("StatusFilterArtist230", null, RankedStatus.Ranked, 0, 100);

        Assert.Single(matching);
        Assert.Empty(nonMatching);
    }

    [Fact]
    public async Task SearchAsync_RespectsOffsetAndAmount()
    {
        await _repository.UpsertAsync(MakeBeatmap(240, 5040, new string('l', 32), "PagingArtist240A", 1.0));
        await _repository.UpsertAsync(MakeBeatmap(241, 5041, new string('m', 32), "PagingArtist240B", 1.0));

        var page1 = await _repository.SearchAsync("PagingArtist240", null, null, 0, 1);
        var page2 = await _repository.SearchAsync("PagingArtist240", null, null, 1, 1);

        Assert.Single(page1);
        Assert.Single(page2);
        Assert.NotEqual(page1[0][0].SetId, page2[0][0].SetId);
    }
}