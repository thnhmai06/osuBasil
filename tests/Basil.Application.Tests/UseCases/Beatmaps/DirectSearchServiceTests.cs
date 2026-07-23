using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Services.Beatmaps;
using Basil.Domain.Beatmaps;
using NSubstitute;

namespace Basil.Application.Tests.UseCases.Beatmaps;

/// <summary>Ported from app/services/direct_search.py's DirectSearchService, DB-backed instead of mirror-backed.</summary>
public class DirectSearchServiceTests
{
    private readonly IMapRepository _maps = Substitute.For<IMapRepository>();

    [Fact]
    public async Task NonTextQuery_PassesNullQueryThrough()
    {
        _maps.SearchAsync(null, null, 0, 100).Returns([]);

        await new DirectSearchService(_maps).SearchAsync(new DirectSearchRequest("Newest", -1, 0));

        await _maps.Received(1).SearchAsync(null, null, 0, 100, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TextQuery_PassedThrough()
    {
        _maps.SearchAsync("camellia", null, 0, 100).Returns([]);

        await new DirectSearchService(_maps).SearchAsync(new DirectSearchRequest("camellia", -1, 0));

        await _maps.Received(1).SearchAsync("camellia", null, 0, 100, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ModeNotMinusOne_FiltersByMode()
    {
        _maps.SearchAsync(null, GameMode.Taiko, 0, 100).Returns([]);

        await new DirectSearchService(_maps).SearchAsync(new DirectSearchRequest("Newest", 1, 0));

        await _maps.Received(1).SearchAsync(null, GameMode.Taiko, 0, 100, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PageNum_MultipliedByOneHundredForOffset()
    {
        _maps.SearchAsync(null, null, 200, 100).Returns([]);

        await new DirectSearchService(_maps).SearchAsync(new DirectSearchRequest("Newest", -1, 2));

        await _maps.Received(1).SearchAsync(null, null, 200, 100, Arg.Any<CancellationToken>());
    }

    private static Beatmap MakeBeatmap(int id, int setId, string version, double diff, string artist = "Artist",
        string title = "Title")
    {
        var mapset = new Mapset(setId, artist, title, "cmyui",
            new DateTime(2020, 3, 15, 10, 30, 0, DateTimeKind.Utc), new DateTime(2020, 3, 15, 10, 30, 0, DateTimeKind.Utc));
        return new Beatmap(
            new string('0', 32), id, mapset, version, "file.osu", TimeSpan.FromSeconds(100), 500, 0, 0,
            new Difficulty(GameMode.Standard, 180, 4, 9, 8, 5, diff));
    }

    [Fact]
    public void Format_EmptyList_ReturnsZero()
    {
        Assert.Equal("0", DirectSearchService.Format([]));
    }

    [Fact]
    public void Format_SingleSetSingleDiff_MatchesFormatString()
    {
        var set = new List<Beatmap> { MakeBeatmap(1, 100, "Hyper", 6.5) };

        var response = DirectSearchService.Format([set]);

        var expectedSetLine =
            "100.osz|Artist|Title|cmyui|4|10.0|2020-03-15 10:30:00|100|0|0|0|0|0|[6.50⭐] Hyper {cs: 4 / od: 8 / ar: 9 / hp: 5}@0";
        Assert.Equal("1\n" + expectedSetLine, response);
    }

    [Fact]
    public void Format_MultipleDiffsInSet_JoinedByComma()
    {
        var set = new List<Beatmap>
        {
            MakeBeatmap(1, 100, "Easy", 2.0),
            MakeBeatmap(2, 100, "Hard", 4.5)
        };

        var response = DirectSearchService.Format([set]);

        Assert.Contains("[2.00⭐] Easy {cs: 4 / od: 8 / ar: 9 / hp: 5}@0,[4.50⭐] Hard {cs: 4 / od: 8 / ar: 9 / hp: 5}@0",
            response);
    }

    [Fact]
    public void Format_PipeInMetadata_ReplacedWithI()
    {
        var set = new List<Beatmap> { MakeBeatmap(1, 100, "Di|ff", 1.0, "Art|ist", "Ti|tle") };

        var response = DirectSearchService.Format([set]);

        Assert.Contains("100.osz|ArtIist|TiItle|cmyui|", response);
        Assert.Contains("DiIff", response);
    }

    [Fact]
    public void Format_100Sets_ReportsCountAs101()
    {
        var sets = Enumerable.Range(0, 100)
            .Select(i => (IReadOnlyList<Beatmap>)new List<Beatmap> { MakeBeatmap(i, i, "Sr", 1.0) }).ToList();

        var response = DirectSearchService.Format(sets);

        Assert.StartsWith("101\n", response);
    }

    [Fact]
    public void Format_99Sets_ReportsLiteralCount()
    {
        var sets = Enumerable.Range(0, 99)
            .Select(i => (IReadOnlyList<Beatmap>)new List<Beatmap> { MakeBeatmap(i, i, "Sr", 1.0) }).ToList();

        var response = DirectSearchService.Format(sets);

        Assert.StartsWith("99\n", response);
    }

    [Fact]
    public void FormatSet_Null_ReturnsEmptyString()
    {
        Assert.Equal("", DirectSearchService.FormatSet(null));
    }

    [Fact]
    public void FormatSet_UsesRawStatusValue_NoDiffsField_NoPipeEscaping()
    {
        var bmap = MakeBeatmap(1, 100, "Hyper", 6.5, "Art|ist");

        var response = DirectSearchService.FormatSet(bmap);

        Assert.Equal("100.osz|Art|ist|Title|cmyui|5|10.0|2020-03-15 10:30:00|100|0|0|0|0|0", response);
    }
}
