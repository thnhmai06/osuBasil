using Bancho.Application.Abstractions;
using Bancho.Application.UseCases.Beatmaps;
using Bancho.Domain;
using NSubstitute;
using Bancho.Application.Abstractions.Beatmaps;
using Bancho.Application.Abstractions.Scores;
using Bancho.Domain.Beatmaps;
using Bancho.Domain.Scores;

namespace Bancho.Application.Tests.UseCases.Beatmaps;

/// <summary>Ported from app/services/maps.py's BeatmapInfoService.fetch_beatmap_info, backing osu-getbeatmapinfo.php.</summary>
public class BeatmapInfoServiceTests
{
    private readonly IMapRepository _maps = Substitute.For<IMapRepository>();
    private readonly IScoreRepository _scores = Substitute.For<IScoreRepository>();

    private static Beatmap MakeBeatmap(string filename, string md5, RankedStatus status = RankedStatus.Ranked) => new(
        md5, 1, 100, "Artist", "Title", "Version", "Creator", DateTime.UtcNow, 100, 500,
        status, false, 0, 0, GameMode.VanillaOsu, 180.0, 4, 8, 9, 5, 6.5, filename);

    [Fact]
    public async Task FetchBeatmapInfoAsync_UnknownFilename_IsSkipped()
    {
        _maps.FetchOneAsync(filename: "missing.osu").Returns((Beatmap?)null);
        var service = new BeatmapInfoService(_maps, _scores);

        var result = await service.FetchBeatmapInfoAsync(["missing.osu"], playerId: 1, GameMode.VanillaOsu);

        Assert.Empty(result);
    }

    [Fact]
    public async Task FetchBeatmapInfoAsync_NoPersonalBest_GradesAllUnplayed()
    {
        var bmap = MakeBeatmap("map.osu", new string('a', 32));
        _maps.FetchOneAsync(filename: "map.osu").Returns(bmap);
        _scores.FetchPersonalBestLeaderboardScoreAsync(bmap.Md5, GameMode.VanillaOsu, 1).Returns((PersonalBestLeaderboardScoreRow?)null);
        var service = new BeatmapInfoService(_maps, _scores);

        var result = await service.FetchBeatmapInfoAsync(["map.osu"], playerId: 1, GameMode.VanillaOsu);

        Assert.Single(result);
        Assert.Equal(["N", "N", "N", "N"], result[0].Grades);
        Assert.Equal(1, result[0].Status); // Ranked -> osu!api status 1
    }

    [Fact]
    public async Task FetchBeatmapInfoAsync_HasPersonalBest_SetsGradeAtVanillaModeIndex()
    {
        var bmap = MakeBeatmap("map.osu", new string('b', 32));
        _maps.FetchOneAsync(filename: "map.osu").Returns(bmap);
        _scores.FetchPersonalBestLeaderboardScoreAsync(bmap.Md5, GameMode.VanillaTaiko, 1).Returns(
            new PersonalBestLeaderboardScoreRow(1, 500_000, 300, 0, 0, 100, 0, 0, 0, true, 0, 0, Grade: "S"));
        var service = new BeatmapInfoService(_maps, _scores);

        var result = await service.FetchBeatmapInfoAsync(["map.osu"], playerId: 1, GameMode.VanillaTaiko);

        Assert.Equal(["N", "S", "N", "N"], result[0].Grades);
    }

    [Fact]
    public async Task FetchBeatmapInfoAsync_PreservesRequestOrderAsIndex()
    {
        var bmapA = MakeBeatmap("a.osu", new string('c', 32));
        var bmapB = MakeBeatmap("b.osu", new string('d', 32));
        _maps.FetchOneAsync(filename: "a.osu").Returns(bmapA);
        _maps.FetchOneAsync(filename: "b.osu").Returns(bmapB);
        var service = new BeatmapInfoService(_maps, _scores);

        var result = await service.FetchBeatmapInfoAsync(["a.osu", "b.osu"], playerId: 1, GameMode.VanillaOsu);

        Assert.Equal(0, result[0].Index);
        Assert.Equal(1, result[1].Index);
    }
}
