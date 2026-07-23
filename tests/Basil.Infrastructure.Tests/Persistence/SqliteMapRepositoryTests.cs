using Basil.Domain.Beatmaps;
using Basil.Infrastructure.Persistence.Repositories;

namespace Basil.Infrastructure.Tests.Persistence;

/// <summary>
///     Ported from app/repositories/maps.py, scoped to what beatmap resolution needs:
///     lookup by id/md5/filename and upsert. `server` is hardcoded to "osu!" everywhere.
/// </summary>
public class SqliteMapRepositoryTests(SqliteFixture fixture) : IClassFixture<SqliteFixture>
{
    private readonly SqliteMapRepository _repository = new(fixture.ConnectionString);
    private readonly SqliteMapsetRepository _mapsetRepository = new(fixture.ConnectionString);

    private static Mapset MakeMapset(int id, string artist = "Camellia",
        string title = "Exit This Earth's Atomosphere", string creator = "cmyui")
    {
        return new Mapset(id, artist, title, creator,
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    private static Beatmap MakeBeatmap(int id, string md5, bool isPrivate = false)
    {
        return new Beatmap(md5, id, MakeMapset(1000 + id), "Hyper",
            $"Camellia - Exit This Earth's Atomosphere (cmyui) [Hyper] {id}.osu", TimeSpan.FromSeconds(120), 500,
            isPrivate, 0, 0, new Difficulty(GameMode.Standard, 180.0, 4.0, 9.0, 8.0, 5.0, 6.5));
    }

    private async Task<Beatmap> UpsertBeatmapAsync(Beatmap beatmap)
    {
        await _mapsetRepository.UpsertAsync(beatmap.Mapset);
        return await _repository.UpsertAsync(beatmap);
    }

    [Fact]
    public async Task UpsertThenFetchByMd5_ReturnsBeatmap()
    {
        var bmap = MakeBeatmap(101, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        await UpsertBeatmapAsync(bmap);
        var fetched = await _repository.FetchOneAsync(md5: bmap.Md5);

        Assert.NotNull(fetched);
        Assert.Equal(bmap, fetched);
    }

    [Fact]
    public async Task FetchById_ReturnsBeatmap()
    {
        var bmap = MakeBeatmap(102, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        await UpsertBeatmapAsync(bmap);

        var fetched = await _repository.FetchOneAsync(bmap.Id);

        Assert.NotNull(fetched);
        Assert.Equal(bmap.Md5, fetched.Md5);
    }

    [Fact]
    public async Task FetchByFilename_ReturnsBeatmap()
    {
        var bmap = MakeBeatmap(103, new string('c', 32));
        await UpsertBeatmapAsync(bmap);

        var fetched = await _repository.FetchOneAsync(filename: bmap.Filename);

        Assert.NotNull(fetched);
        Assert.Equal(bmap.Id, fetched.Id);
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
        await UpsertBeatmapAsync(bmap);

        var updated = bmap with { Plays = 42 };
        await UpsertBeatmapAsync(updated);

        var fetched = await _repository.FetchOneAsync(bmap.Id);
        Assert.Equal(42, fetched!.Plays);
    }

    [Fact]
    public async Task Upsert_UnresolvedId_ResolvesFromLocalIdFloor()
    {
        var bmap = MakeBeatmap(0, "aa000000000000000000000000000a");

        await _mapsetRepository.UpsertAsync(bmap.Mapset);
        var resolved = await _repository.UpsertAsync(bmap);

        Assert.True(resolved.Id >= Beatmap.LocalIdFloor);
        var fetched = await _repository.FetchOneAsync(md5: bmap.Md5);
        Assert.Equal(resolved.Id, fetched!.Id);
    }

    [Fact]
    public async Task Upsert_ExistingMd5_KeepsOriginalId_EvenWhenPassedADifferentId()
    {
        var original = MakeBeatmap(107, "bb000000000000000000000000000b");
        var firstResolved = await UpsertBeatmapAsync(original);

        var reupserted = original with { Id = 999_999, Plays = 7 };
        var secondResolved = await UpsertBeatmapAsync(reupserted);

        Assert.Equal(firstResolved.Id, secondResolved.Id);
        Assert.Equal(7, secondResolved.Plays);
    }

    [Fact]
    public async Task DeleteByMd5_RemovesRow()
    {
        var bmap = MakeBeatmap(105, "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee");
        await UpsertBeatmapAsync(bmap);

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
        await UpsertBeatmapAsync(bmap);

        var fetched = await _repository.FetchOneAsync(setId: bmap.Mapset.Id);

        Assert.NotNull(fetched);
        Assert.Equal(bmap.Mapset.Id, fetched.Mapset.Id);
    }

    [Fact]
    public async Task FetchOne_PrivateBeatmap_HiddenByDefault_VisibleWithIncludePrivate()
    {
        var bmap = MakeBeatmap(108, "gg00000000000000000000000000gg", isPrivate: true);
        await UpsertBeatmapAsync(bmap);

        Assert.Null(await _repository.FetchOneAsync(bmap.Id));
        var fetched = await _repository.FetchOneAsync(bmap.Id, includePrivate: true);
        Assert.NotNull(fetched);
        Assert.True(fetched.IsPrivate);
    }

    [Fact]
    public async Task FetchAllBySetId_ExcludesPrivateByDefault_IncludesWithFlag()
    {
        var setId = 5050;
        var mapset = MakeMapset(setId);
        var visible = new Beatmap(new string('n', 32), 250, mapset, "Normal", "n.osu",
            TimeSpan.FromSeconds(60), 500, false, 0, 0, new Difficulty(GameMode.Standard, 180.0, 4.0, 9.0, 8.0, 5.0, 3.0));
        var frozen = new Beatmap(new string('o', 32), 251, mapset, "Hidden", "o.osu",
            TimeSpan.FromSeconds(60), 500, true, 0, 0, new Difficulty(GameMode.Standard, 180.0, 4.0, 9.0, 8.0, 5.0, 3.0));
        await UpsertBeatmapAsync(visible);
        await UpsertBeatmapAsync(frozen);

        var defaultResult = await _repository.FetchAllBySetIdAsync(setId);
        var includingPrivate = await _repository.FetchAllBySetIdAsync(setId, includePrivate: true);

        Assert.Single(defaultResult);
        Assert.Equal(2, includingPrivate.Count);
    }

    private static Beatmap MakeBeatmap(int id, int setId, string md5, string artist, double diff,
        GameMode mode = GameMode.Standard)
    {
        return new Beatmap(md5, id, MakeMapset(setId, artist: artist, title: "Title"),
            $"Diff{id}", $"{artist} - Title (cmyui) [Sr{id}].osu", TimeSpan.FromSeconds(120), 500, false, 0, 0,
            new Difficulty(mode, 180.0, 4.0, 9.0, 8.0, 5.0, diff));
    }

    [Fact]
    public async Task SearchAsync_GroupsBySetId_NewestFirst_DiffsAscendingWithinSet()
    {
        var setA = 5001;
        var setB = 5002; // higher set_id -> newer -> should come first
        await UpsertBeatmapAsync(MakeBeatmap(201, setA, new string('f', 32), "Zeta Artist", 5.0));
        await UpsertBeatmapAsync(MakeBeatmap(202, setA, new string('g', 32), "Zeta Artist", 2.0));
        await UpsertBeatmapAsync(MakeBeatmap(203, setB, new string('h', 32), "Alpha Artist", 3.0));

        var results = await _repository.SearchAsync(null, null, 0, 100);
        var relevant = results.Where(set => set[0].Mapset.Id is 5001 or 5002).ToList();

        Assert.Equal(5002, relevant[0][0].Mapset.Id);
        Assert.Equal(5001, relevant[1][0].Mapset.Id);
        Assert.Equal(2, relevant[1].Count);
        Assert.True(relevant[1][0].Difficulty.Sr < relevant[1][1].Difficulty.Sr);
    }

    [Fact]
    public async Task SearchAsync_FiltersByQueryText_MatchesArtistTitleOrCreator()
    {
        var setId = 5010;
        await UpsertBeatmapAsync(MakeBeatmap(210, setId, new string('i', 32), "UniqueArtistName210", 1.0));

        var results = await _repository.SearchAsync("UniqueArtistName210", null, 0, 100);

        Assert.Single(results);
        Assert.Equal(setId, results[0][0].Mapset.Id);
    }

    [Fact]
    public async Task SearchAsync_FiltersByMode()
    {
        var setId = 5020;
        await UpsertBeatmapAsync(MakeBeatmap(220, setId, new string('j', 32), "ModeFilterArtist220", 1.0,
            GameMode.Taiko));

        var matching = await _repository.SearchAsync("ModeFilterArtist220", GameMode.Taiko, 0, 100);
        var nonMatching = await _repository.SearchAsync("ModeFilterArtist220", GameMode.Catch, 0, 100);

        Assert.Single(matching);
        Assert.Empty(nonMatching);
    }

    [Fact]
    public async Task SearchAsync_RespectsOffsetAndAmount()
    {
        await UpsertBeatmapAsync(MakeBeatmap(240, 5040, new string('l', 32), "PagingArtist240A", 1.0));
        await UpsertBeatmapAsync(MakeBeatmap(241, 5041, new string('m', 32), "PagingArtist240B", 1.0));

        var page1 = await _repository.SearchAsync("PagingArtist240", null, 0, 1);
        var page2 = await _repository.SearchAsync("PagingArtist240", null, 1, 1);

        Assert.Single(page1);
        Assert.Single(page2);
        Assert.NotEqual(page1[0][0].Mapset.Id, page2[0][0].Mapset.Id);
    }

    [Fact]
    public async Task SearchAsync_ExcludesPrivateBeatmaps()
    {
        var setId = 5060;
        var mapset = MakeMapset(setId, artist: "PrivateSearchArtist260");
        var frozen = new Beatmap(new string('p', 32), 260, mapset, "Diff", "p.osu",
            TimeSpan.FromSeconds(120), 500, true, 0, 0, new Difficulty(GameMode.Standard, 180.0, 4.0, 9.0, 8.0, 5.0, 1.0));
        await UpsertBeatmapAsync(frozen);

        var results = await _repository.SearchAsync("PrivateSearchArtist260", null, 0, 100);

        Assert.Empty(results);
    }
}
