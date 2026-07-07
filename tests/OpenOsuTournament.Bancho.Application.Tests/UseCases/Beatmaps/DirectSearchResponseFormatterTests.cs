using OpenOsuTournament.Bancho.Application.UseCases.Beatmaps;
using OpenOsuTournament.Bancho.Domain.Beatmaps;

namespace OpenOsuTournament.Bancho.Application.Tests.UseCases.Beatmaps;

/// <summary>
///     Ported from app/api/domains/osu.py's format_direct_search_response. No golden fixture exists
///     in bancho.py for this response — expected strings are hand-built from the documented format.
/// </summary>
public class DirectSearchResponseFormatterTests
{
    private static Beatmap MakeBeatmap(int id, int setId, string version, double diff, string artist = "Artist",
        string title = "Title")
    {
        return new Beatmap(
            new string('0', 32), id, setId, artist, title, version, "cmyui",
            new DateTime(2020, 3, 15, 10, 30, 0, DateTimeKind.Utc), 100, 500,
            RankedStatus.Ranked, false, 0, 0, GameMode.VanillaOsu,
            180, 4, 8, 9, 5, diff, "file.osu");
    }

    [Fact]
    public void Format_EmptyList_ReturnsZero()
    {
        Assert.Equal("0", DirectSearchResponseFormatter.Format([]));
    }

    [Fact]
    public void Format_SingleSetSingleDiff_MatchesFormatString()
    {
        var set = new List<Beatmap> { MakeBeatmap(1, 100, "Hyper", 6.5) };

        var response = DirectSearchResponseFormatter.Format([set]);

        var expectedSetLine =
            "100.osz|Artist|Title|cmyui|1|10.0|2020-03-15 10:30:00|100|0|0|0|0|0|[6.50⭐] Hyper {cs: 4 / od: 8 / ar: 9 / hp: 5}@0";
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

        var response = DirectSearchResponseFormatter.Format([set]);

        Assert.Contains("[2.00⭐] Easy {cs: 4 / od: 8 / ar: 9 / hp: 5}@0,[4.50⭐] Hard {cs: 4 / od: 8 / ar: 9 / hp: 5}@0",
            response);
    }

    [Fact]
    public void Format_PipeInMetadata_ReplacedWithI()
    {
        var set = new List<Beatmap> { MakeBeatmap(1, 100, "Di|ff", 1.0, "Art|ist", "Ti|tle") };

        var response = DirectSearchResponseFormatter.Format([set]);

        Assert.Contains("100.osz|ArtIist|TiItle|cmyui|", response);
        Assert.Contains("DiIff", response);
    }

    [Fact]
    public void Format_100Sets_ReportsCountAs101()
    {
        var sets = Enumerable.Range(0, 100)
            .Select(i => (IReadOnlyList<Beatmap>)new List<Beatmap> { MakeBeatmap(i, i, "Diff", 1.0) }).ToList();

        var response = DirectSearchResponseFormatter.Format(sets);

        Assert.StartsWith("101\n", response);
    }

    [Fact]
    public void Format_99Sets_ReportsLiteralCount()
    {
        var sets = Enumerable.Range(0, 99)
            .Select(i => (IReadOnlyList<Beatmap>)new List<Beatmap> { MakeBeatmap(i, i, "Diff", 1.0) }).ToList();

        var response = DirectSearchResponseFormatter.Format(sets);

        Assert.StartsWith("99\n", response);
    }

    [Fact]
    public void FormatSet_Null_ReturnsEmptyString()
    {
        Assert.Equal("", DirectSearchResponseFormatter.FormatSet(null));
    }

    [Fact]
    public void FormatSet_UsesRawStatusValue_NoDiffsField_NoPipeEscaping()
    {
        var bmap = MakeBeatmap(1, 100, "Hyper", 6.5, "Art|ist");
        var bmapWithLoved = bmap with { Status = RankedStatus.Loved };

        var response = DirectSearchResponseFormatter.FormatSet(bmapWithLoved);

        Assert.Equal("100.osz|Art|ist|Title|cmyui|5|10.0|2020-03-15 10:30:00|100|0|0|0|0|0", response);
    }
}