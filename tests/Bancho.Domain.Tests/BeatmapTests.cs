using Bancho.Domain.Beatmaps;

namespace Bancho.Domain.Tests;

/// <summary>Ported from app/objects/beatmap.py's Beatmap fields + the properties Phase 5 actually consumes.</summary>
public class BeatmapTests
{
    private static Beatmap MakeBeatmap(RankedStatus status)
    {
        return new Beatmap(
            "d41d8cd98f00b204e9800998ecf8427e",
            321,
            100,
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
            "Camellia - Exit This Earth's Atomosphere (cmyui) [Hyper].osu");
    }

    [Fact]
    public void FullName_FormatsArtistTitleVersion()
    {
        var bmap = MakeBeatmap(RankedStatus.Ranked);

        Assert.Equal("Camellia - Exit This Earth's Atomosphere [Hyper]", bmap.FullName);
    }

    [Theory]
    [InlineData(RankedStatus.NotSubmitted, false)]
    [InlineData(RankedStatus.Pending, false)]
    [InlineData(RankedStatus.UpdateAvailable, false)]
    [InlineData(RankedStatus.Ranked, true)]
    [InlineData(RankedStatus.Approved, true)]
    [InlineData(RankedStatus.Qualified, true)]
    [InlineData(RankedStatus.Loved, true)]
    public void HasLeaderboard_MatchesGetscoresGate(RankedStatus status, bool expected)
    {
        // Ported from the literal `bmap.status < RankedStatus.Ranked` check in
        // beatmap_leaderboards.py:119 — NOT the same as Beatmap.has_leaderboard's
        // (Ranked, Approved, Loved) tuple property, which excludes Qualified despite
        // Qualified's int value being greater than Ranked's. The getscores endpoint
        // uses the raw comparison, so Qualified maps do get a leaderboard here.
        var bmap = MakeBeatmap(status);

        Assert.Equal(expected, bmap.HasLeaderboard);
    }

    [Theory]
    [InlineData(RankedStatus.NotSubmitted, false)]
    [InlineData(RankedStatus.Pending, false)]
    [InlineData(RankedStatus.UpdateAvailable, false)]
    [InlineData(RankedStatus.Ranked, true)]
    [InlineData(RankedStatus.Approved, true)]
    [InlineData(RankedStatus.Qualified, false)]
    [InlineData(RankedStatus.Loved, true)]
    public void HasLeaderboardStrict_MatchesHasLeaderboardProperty_ExcludesQualified(RankedStatus status, bool expected)
    {
        var bmap = MakeBeatmap(status);

        Assert.Equal(expected, bmap.HasLeaderboardStrict);
    }

    [Theory]
    [InlineData(RankedStatus.NotSubmitted, false)]
    [InlineData(RankedStatus.Pending, false)]
    [InlineData(RankedStatus.UpdateAvailable, false)]
    [InlineData(RankedStatus.Ranked, true)]
    [InlineData(RankedStatus.Approved, true)]
    [InlineData(RankedStatus.Qualified, false)]
    [InlineData(RankedStatus.Loved, false)]
    public void AwardsRankedScore_MatchesAwardsRankedPp_ExcludesLovedAndQualified(RankedStatus status, bool expected)
    {
        var bmap = MakeBeatmap(status);

        Assert.Equal(expected, bmap.AwardsRankedScore);
    }
}