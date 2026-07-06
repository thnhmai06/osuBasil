using Bancho.Domain;
using Bancho.Infrastructure.Persistence;

namespace Bancho.Infrastructure.Tests.Persistence;

/// <summary>
/// Ported from app/repositories/maps.py, scoped to what beatmap resolution (Phase 5) needs:
/// lookup by id/md5/filename and upsert. `server` is hardcoded to "osu!" everywhere.
/// </summary>
public class MySqlMapRepositoryTests : IClassFixture<MySqlFixture>
{
    private readonly MySqlMapRepository _repository;

    public MySqlMapRepositoryTests(MySqlFixture fixture)
    {
        _repository = new MySqlMapRepository(fixture.ConnectionString);
    }

    private static Beatmap MakeBeatmap(int id, string md5, RankedStatus status = RankedStatus.Ranked) => new(
        Md5: md5,
        Id: id,
        SetId: 1000 + id,
        Artist: "Camellia",
        Title: "Exit This Earth's Atomosphere",
        Version: "Hyper",
        Creator: "cmyui",
        LastUpdate: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        TotalLength: 120,
        MaxCombo: 500,
        Status: status,
        Frozen: false,
        Plays: 0,
        Passes: 0,
        Mode: GameMode.VanillaOsu,
        Bpm: 180.0,
        Cs: 4.0,
        Od: 8.0,
        Ar: 9.0,
        Hp: 5.0,
        Diff: 6.5,
        Filename: $"Camellia - Exit This Earth's Atomosphere (cmyui) [Hyper] {id}.osu");

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

        var fetched = await _repository.FetchOneAsync(id: bmap.Id);

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

        var fetched = await _repository.FetchOneAsync(id: bmap.Id);
        Assert.Equal(RankedStatus.Loved, fetched!.Status);
        Assert.Equal(42, fetched.Plays);
    }

    [Fact]
    public async Task DeleteByMd5_RemovesRow()
    {
        var bmap = MakeBeatmap(105, "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee");
        await _repository.UpsertAsync(bmap);

        await _repository.DeleteByMd5Async(bmap.Md5);

        Assert.Null(await _repository.FetchOneAsync(id: bmap.Id));
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

    private static Beatmap MakeBeatmap(int id, int setId, string md5, string artist, double diff, GameMode mode = GameMode.VanillaOsu, RankedStatus status = RankedStatus.Ranked) => new(
        Md5: md5, Id: id, SetId: setId, Artist: artist, Title: "Title", Version: $"Diff{id}", Creator: "cmyui",
        LastUpdate: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), TotalLength: 120, MaxCombo: 500,
        Status: status, Frozen: false, Plays: 0, Passes: 0, Mode: mode, Bpm: 180.0, Cs: 4.0, Od: 8.0, Ar: 9.0, Hp: 5.0,
        Diff: diff, Filename: $"{artist} - Title (cmyui) [Diff{id}].osu");

    [Fact]
    public async Task SearchAsync_GroupsBySetId_NewestFirst_DiffsAscendingWithinSet()
    {
        var setA = 5001;
        var setB = 5002; // higher set_id -> newer -> should come first
        await _repository.UpsertAsync(MakeBeatmap(201, setA, new string('f', 32), "Zeta Artist", diff: 5.0));
        await _repository.UpsertAsync(MakeBeatmap(202, setA, new string('g', 32), "Zeta Artist", diff: 2.0));
        await _repository.UpsertAsync(MakeBeatmap(203, setB, new string('h', 32), "Alpha Artist", diff: 3.0));

        var results = await _repository.SearchAsync(query: null, mode: null, status: null, offset: 0, amount: 100);
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
        await _repository.UpsertAsync(MakeBeatmap(210, setId, new string('i', 32), "UniqueArtistName210", diff: 1.0));

        var results = await _repository.SearchAsync(query: "UniqueArtistName210", mode: null, status: null, offset: 0, amount: 100);

        Assert.Single(results);
        Assert.Equal(setId, results[0][0].SetId);
    }

    [Fact]
    public async Task SearchAsync_FiltersByMode()
    {
        var setId = 5020;
        await _repository.UpsertAsync(MakeBeatmap(220, setId, new string('j', 32), "ModeFilterArtist220", diff: 1.0, mode: GameMode.VanillaTaiko));

        var matching = await _repository.SearchAsync(query: "ModeFilterArtist220", mode: GameMode.VanillaTaiko, status: null, offset: 0, amount: 100);
        var nonMatching = await _repository.SearchAsync(query: "ModeFilterArtist220", mode: GameMode.VanillaCatch, status: null, offset: 0, amount: 100);

        Assert.Single(matching);
        Assert.Empty(nonMatching);
    }

    [Fact]
    public async Task SearchAsync_FiltersByStatus()
    {
        var setId = 5030;
        await _repository.UpsertAsync(MakeBeatmap(230, setId, new string('k', 32), "StatusFilterArtist230", diff: 1.0, status: RankedStatus.Loved));

        var matching = await _repository.SearchAsync(query: "StatusFilterArtist230", mode: null, status: RankedStatus.Loved, offset: 0, amount: 100);
        var nonMatching = await _repository.SearchAsync(query: "StatusFilterArtist230", mode: null, status: RankedStatus.Ranked, offset: 0, amount: 100);

        Assert.Single(matching);
        Assert.Empty(nonMatching);
    }

    [Fact]
    public async Task SearchAsync_RespectsOffsetAndAmount()
    {
        await _repository.UpsertAsync(MakeBeatmap(240, 5040, new string('l', 32), "PagingArtist240A", diff: 1.0));
        await _repository.UpsertAsync(MakeBeatmap(241, 5041, new string('m', 32), "PagingArtist240B", diff: 1.0));

        var page1 = await _repository.SearchAsync(query: "PagingArtist240", mode: null, status: null, offset: 0, amount: 1);
        var page2 = await _repository.SearchAsync(query: "PagingArtist240", mode: null, status: null, offset: 1, amount: 1);

        Assert.Single(page1);
        Assert.Single(page2);
        Assert.NotEqual(page1[0][0].SetId, page2[0][0].SetId);
    }
}
