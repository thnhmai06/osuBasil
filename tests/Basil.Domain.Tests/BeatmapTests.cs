using Basil.Domain.Beatmaps;

namespace Basil.Domain.Tests;

/// <summary>Ported from app/objects/beatmap.py's Beatmap fields + the properties actually consumed by score submission and leaderboards.</summary>
public class BeatmapTests
{
    private static Beatmap MakeBeatmap()
    {
        var mapset = new Mapset(100, "Camellia", "Exit This Earth's Atomosphere", "cmyui",
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        return new Beatmap(
            "d41d8cd98f00b204e9800998ecf8427e",
            321,
            mapset,
            "Hyper",
            "Camellia - Exit This Earth's Atomosphere (cmyui) [Hyper].osu",
            TimeSpan.FromSeconds(120), 500, 0, 0, new Difficulty(GameMode.Standard, 180.0, 4.0, 9.0, 8.0, 5.0, 6.5));
    }

    [Fact]
    public void FullName_FormatsArtistTitleVersion()
    {
        var bmap = MakeBeatmap();

        Assert.Equal("Camellia - Exit This Earth's Atomosphere [Hyper]", bmap.FullName);
    }

    [Fact]
    public void Status_AlwaysLoved()
    {
        var bmap = MakeBeatmap();

        Assert.Equal(RankedStatus.Loved, bmap.Mapset.Status);
    }
}
