using NSubstitute;
using OpenOsuTournament.Bancho.Application.Abstractions.Beatmaps;
using OpenOsuTournament.Bancho.Application.UseCases.Beatmaps;
using OpenOsuTournament.Bancho.Domain.Beatmaps;

namespace OpenOsuTournament.Bancho.Application.Tests.UseCases.Beatmaps;

/// <summary>
///     Ported from Beatmap.from_md5 + BeatmapLeaderboardService._classify_missing_beatmap, collapsed
///     to DB-only resolution (no osu!api fallback — this server runs fully offline).
/// </summary>
public class EnsureBeatmapUseCaseTests
{
    private readonly IMapRepository _maps = Substitute.For<IMapRepository>();

    private static Beatmap MakeBeatmap(string md5)
    {
        return new Beatmap(
            md5, 1, 100, "Artist", "Title", "Version", "Creator", DateTime.UtcNow, 100, 500,
            RankedStatus.Ranked, false, 0, 0, GameMode.VanillaOsu, 180.0, 4, 8, 9, 5, 6.5, "file.osu");
    }

    [Fact]
    public async Task Md5FoundInDb_ReturnsFound()
    {
        var bmap = MakeBeatmap(new string('1', 32));
        _maps.FetchOneAsync(md5: bmap.Md5).Returns(bmap);

        var result = await new EnsureBeatmapUseCase(_maps).ResolveAsync(bmap.Md5, bmap.Filename);

        Assert.Equal(BeatmapResolutionResultCode.Found, result.Code);
        Assert.Equal(bmap, result.Beatmap);
    }

    [Fact]
    public async Task Md5NotFound_FilenameExists_ReturnsNeedsUpdate()
    {
        var md5 = new string('2', 32);
        _maps.FetchOneAsync(md5: md5).Returns((Beatmap?)null);
        _maps.FetchOneAsync(filename: "old-version.osu").Returns(MakeBeatmap(new string('3', 32)));

        var result = await new EnsureBeatmapUseCase(_maps).ResolveAsync(md5, "old-version.osu");

        Assert.Equal(BeatmapResolutionResultCode.NeedsUpdate, result.Code);
        Assert.Null(result.Beatmap);
    }

    [Fact]
    public async Task Md5NotFound_FilenameAlsoNotFound_ReturnsNotSubmitted()
    {
        var md5 = new string('4', 32);
        _maps.FetchOneAsync(md5: md5).Returns((Beatmap?)null);
        _maps.FetchOneAsync(filename: "unknown.osu").Returns((Beatmap?)null);

        var result = await new EnsureBeatmapUseCase(_maps).ResolveAsync(md5, "unknown.osu");

        Assert.Equal(BeatmapResolutionResultCode.NotSubmitted, result.Code);
        Assert.Null(result.Beatmap);
    }
}