using Bancho.Application.Abstractions;
using Bancho.Application.UseCases.Beatmaps;
using Bancho.Domain;
using Bancho.Application.Abstractions.Scores;
using Bancho.Domain.Beatmaps;

namespace Bancho.Application.Tests.UseCases.Beatmaps;

/// <summary>
/// Ported from app/api/domains/osu.py's format_scores_response/SCORE_LISTING_FMTSTR. No golden
/// fixture exists in bancho.py for this response — expected strings are hand-built from the
/// documented format, unverified against a real client (see GetScoresResponseFormatter's doc).
/// </summary>
public class GetScoresResponseFormatterTests
{
    [Fact]
    public void NotSubmitted_IsFixedString()
    {
        Assert.Equal("-1|false", GetScoresResponseFormatter.NotSubmitted);
    }

    [Fact]
    public void NeedsUpdate_IsFixedString()
    {
        Assert.Equal("1|false", GetScoresResponseFormatter.NeedsUpdate);
    }

    [Fact]
    public void NoLeaderboard_FormatsStatusIntWithFalse()
    {
        Assert.Equal("0|false", GetScoresResponseFormatter.NoLeaderboard(RankedStatus.Pending));
        Assert.Equal("4|false", GetScoresResponseFormatter.NoLeaderboard(RankedStatus.Qualified));
    }

    [Fact]
    public void Found_NoScores_ProducesHeaderAndTwoBlankLines()
    {
        var result = new BeatmapLeaderboardResult(
            BeatmapLeaderboardResultCode.Found,
            RankedStatus: RankedStatus.Ranked, BeatmapId: 321, BeatmapSetId: 100,
            BeatmapName: "Artist - Title [Version]", BeatmapRating: 0.0, ScoreRows: []);

        var response = GetScoresResponseFormatter.Found(result);

        Assert.Equal("2|false|321|100|0|0|\n0\nArtist - Title [Version]\n0\n\n", response);
    }

    [Fact]
    public void Found_WithScoresNoPersonalBest_LeavesPersonalBestLineBlank()
    {
        var row = new BeatmapLeaderboardScoreRow(5, 900_000, 500, 5, 10, 300, 0, 1, 2, true, 8, 1700000000, 42, "bob");
        var result = new BeatmapLeaderboardResult(
            BeatmapLeaderboardResultCode.Found,
            RankedStatus: RankedStatus.Ranked, BeatmapId: 321, BeatmapSetId: 100,
            BeatmapName: "Artist - Title [Version]", BeatmapRating: 7.5, ScoreRows: [row]);

        var response = GetScoresResponseFormatter.Found(result);

        var expected = "2|false|321|100|1|0|\n0\nArtist - Title [Version]\n7.5\n\n"
            + "5|bob|900000|500|5|10|300|0|1|2|1|8|42|1|1700000000|1";
        Assert.Equal(expected, response);
    }

    [Fact]
    public void Found_WithPersonalBest_UsesPlayerNameIdAndRank()
    {
        var row = new BeatmapLeaderboardScoreRow(5, 900_000, 500, 5, 10, 300, 0, 1, 2, true, 8, 1700000000, 1, "cmyui");
        var personalBest = new PersonalBestLeaderboardScoreListing(
            5, 900_000, 500, 5, 10, 300, 0, 1, 2, true, 8, 1700000000, Rank: 1, UserId: 1, Name: "cmyui");
        var result = new BeatmapLeaderboardResult(
            BeatmapLeaderboardResultCode.Found,
            RankedStatus: RankedStatus.Ranked, BeatmapId: 321, BeatmapSetId: 100,
            BeatmapName: "Artist - Title [Version]", BeatmapRating: 7.5, ScoreRows: [row], PersonalBest: personalBest);

        var response = GetScoresResponseFormatter.Found(result);

        var expectedPersonalBestLine = "5|cmyui|900000|500|5|10|300|0|1|2|1|8|1|1|1700000000|1";
        var expectedScoreLine = "5|cmyui|900000|500|5|10|300|0|1|2|1|8|1|1|1700000000|1";
        var expected = "2|false|321|100|1|0|\n0\nArtist - Title [Version]\n7.5\n"
            + expectedPersonalBestLine + "\n" + expectedScoreLine;
        Assert.Equal(expected, response);
    }

    [Fact]
    public void Found_MultipleScores_RanksSequentiallyFromOne()
    {
        var rows = new[]
        {
            new BeatmapLeaderboardScoreRow(1, 900_000, 500, 0, 0, 300, 0, 0, 0, false, 0, 1, 10, "alice"),
            new BeatmapLeaderboardScoreRow(2, 800_000, 500, 0, 0, 300, 0, 0, 0, false, 0, 2, 11, "bob"),
        };
        var result = new BeatmapLeaderboardResult(
            BeatmapLeaderboardResultCode.Found,
            RankedStatus: RankedStatus.Ranked, BeatmapId: 321, BeatmapSetId: 100,
            BeatmapName: "Artist - Title [Version]", BeatmapRating: 0.0, ScoreRows: rows);

        var response = GetScoresResponseFormatter.Found(result);
        var lines = response.Split('\n');

        Assert.Equal("1|alice|900000|500|0|0|300|0|0|0|0|0|10|1|1|1", lines[^2]);
        Assert.Equal("2|bob|800000|500|0|0|300|0|0|0|0|0|11|2|2|1", lines[^1]);
    }
}
