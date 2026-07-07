using Bancho.Domain.Beatmaps;
namespace Bancho.Domain.Tests;

/// <summary>Ported from app/objects/beatmap.py's Beatmap fields + the properties Phase 5 actually consumes.</summary>
public class BeatmapTests
{
    private static Beatmap MakeBeatmap(RankedStatus status) => new(
        Md5: "d41d8cd98f00b204e9800998ecf8427e",
        Id: 321,
        SetId: 100,
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
        Filename: "Camellia - Exit This Earth's Atomosphere (cmyui) [Hyper].osu");

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
