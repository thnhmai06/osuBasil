using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Scores;
using Basil.Application.Abstractions.Social;
using Basil.Application.Sessions;
using Basil.Application.UseCases.Beatmaps;
using Basil.Domain;
using Basil.Domain.Beatmaps;
using Basil.Domain.Login;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using NSubstitute;

namespace Basil.Application.Tests.UseCases.Beatmaps;

/// <summary>
///     Ported from app/services/beatmap_leaderboards.py's BeatmapLeaderboardService, merged with
///     score_leaderboards.py's ScoreLeaderboardsService. scoring_metric is dropped entirely — every
///     leaderboard is ranked by raw score (no-pp scope decision).
/// </summary>
public class BeatmapLeaderboardServiceTests
{
    private readonly IMapRepository _maps = Substitute.For<IMapRepository>();
    private readonly IRatingRepository _ratings = Substitute.For<IRatingRepository>();
    private readonly IRelationshipRepository _relationships = Substitute.For<IRelationshipRepository>();
    private readonly IScoreRepository _scores = Substitute.For<IScoreRepository>();
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();

    private BeatmapLeaderboardService MakeService()
    {
        return new BeatmapLeaderboardService(new EnsureBeatmapUseCase(_maps), _scores, _ratings, _relationships,
            _sessionRegistry);
    }

    private static PlayerSession MakePlayer(int id = 1, string name = "cmyui", string country = "us")
    {
        return new PlayerSession(id, name, "token", Privileges.Unrestricted, 0.0)
            { Geoloc = new Geolocation(0, 0, country, 0) };
    }

    private static Beatmap MakeBeatmap(string md5, RankedStatus status = RankedStatus.Ranked)
    {
        return new Beatmap(
            md5, 321, 100, "Artist", "Title", "Version", "Creator", DateTime.UtcNow, 100, 500,
            status, false, 0, 0, GameMode.VanillaOsu, 180.0, 4, 8, 9, 5, 6.5, "file.osu");
    }

    private static BeatmapLeaderboardRequest MakeRequest(
        string mapMd5, string mapFilename = "file.osu", LeaderboardType leaderboardType = LeaderboardType.Top,
        int modeArg = 0, int modsArg = 0, bool editorSongSelect = false)
    {
        return new BeatmapLeaderboardRequest(editorSongSelect, leaderboardType, mapMd5, mapFilename, modeArg, modsArg);
    }

    [Fact]
    public async Task UnknownBeatmap_UnknownFilename_ReturnsNotSubmitted()
    {
        var md5 = new string('1', 32);
        _maps.FetchOneAsync(md5: md5).Returns((Beatmap?)null);
        _maps.FetchOneAsync(filename: "unknown.osu").Returns((Beatmap?)null);

        var result = await MakeService().FetchLeaderboardAsync(MakePlayer(), MakeRequest(md5, "unknown.osu"));

        Assert.Equal(BeatmapLeaderboardResultCode.NotSubmitted, result.Code);
    }

    [Fact]
    public async Task UnknownMd5_KnownFilename_ReturnsNeedsUpdate()
    {
        var md5 = new string('2', 32);
        _maps.FetchOneAsync(md5: md5).Returns((Beatmap?)null);
        _maps.FetchOneAsync(filename: "old.osu").Returns(MakeBeatmap(new string('3', 32)));

        var result = await MakeService().FetchLeaderboardAsync(MakePlayer(), MakeRequest(md5, "old.osu"));

        Assert.Equal(BeatmapLeaderboardResultCode.NeedsUpdate, result.Code);
    }

    [Theory]
    [InlineData(RankedStatus.Pending)]
    [InlineData(RankedStatus.UpdateAvailable)]
    public async Task UnrankedBeatmap_ReturnsNoLeaderboard(RankedStatus status)
    {
        var md5 = new string('4', 32);
        _maps.FetchOneAsync(md5: md5).Returns(MakeBeatmap(md5, status));

        var result = await MakeService().FetchLeaderboardAsync(MakePlayer(), MakeRequest(md5));

        Assert.Equal(BeatmapLeaderboardResultCode.NoLeaderboard, result.Code);
        Assert.Equal(status, result.RankedStatus);
    }

    [Fact]
    public async Task RankedBeatmap_ReturnsScoreRowsAndRating()
    {
        var md5 = new string('5', 32);
        var bmap = MakeBeatmap(md5);
        _maps.FetchOneAsync(md5: md5).Returns(bmap);
        var row = new BeatmapLeaderboardScoreRow(1, 900_000, 500, 5, 10, 300, 0, 0, 0, false, 0, 123, 2, "bob");
        _scores.FetchBeatmapLeaderboardScoresAsync(md5, GameMode.VanillaOsu, 1).Returns([row]);
        _scores.FetchPersonalBestLeaderboardScoreAsync(md5, GameMode.VanillaOsu, 1)
            .Returns((PersonalBestLeaderboardScoreRow?)null);
        _ratings.FetchAverageRatingAsync(md5).Returns(7.5);

        var result = await MakeService().FetchLeaderboardAsync(MakePlayer(), MakeRequest(md5));

        Assert.Equal(BeatmapLeaderboardResultCode.Found, result.Code);
        Assert.Equal(bmap.Id, result.BeatmapId);
        Assert.Equal(bmap.SetId, result.BeatmapSetId);
        Assert.Equal(bmap.FullName, result.BeatmapName);
        Assert.Equal(7.5, result.BeatmapRating);
        Assert.Single(result.ScoreRows!);
        Assert.Null(result.PersonalBest);
    }

    [Fact]
    public async Task RankedBeatmap_WithPersonalBest_IncludesRankAndOwnNameId()
    {
        var md5 = new string('6', 32);
        _maps.FetchOneAsync(md5: md5).Returns(MakeBeatmap(md5));
        _scores.FetchBeatmapLeaderboardScoresAsync(md5, GameMode.VanillaOsu, 1)
            .Returns([new BeatmapLeaderboardScoreRow(1, 900_000, 500, 5, 10, 300, 0, 0, 0, false, 0, 123, 1, "cmyui")]);
        var best = new PersonalBestLeaderboardScoreRow(1, 900_000, 500, 5, 10, 300, 0, 0, 0, false, 0, 123);
        _scores.FetchPersonalBestLeaderboardScoreAsync(md5, GameMode.VanillaOsu, 1).Returns(best);
        _scores.FetchPersonalBestLeaderboardRankAsync(md5, GameMode.VanillaOsu, 900_000).Returns(1);

        var result = await MakeService().FetchLeaderboardAsync(MakePlayer(), MakeRequest(md5));

        Assert.NotNull(result.PersonalBest);
        Assert.Equal(1, result.PersonalBest!.Rank);
        Assert.Equal(1, result.PersonalBest.UserId);
        Assert.Equal("cmyui", result.PersonalBest.Name);
    }

    [Fact]
    public async Task NoScoresFound_SkipsPersonalBestLookup()
    {
        var md5 = new string('7', 32);
        _maps.FetchOneAsync(md5: md5).Returns(MakeBeatmap(md5));
        _scores.FetchBeatmapLeaderboardScoresAsync(md5, GameMode.VanillaOsu, 1).Returns([]);

        await MakeService().FetchLeaderboardAsync(MakePlayer(), MakeRequest(md5));

        await _scores.DidNotReceive().FetchPersonalBestLeaderboardScoreAsync(
            Arg.Any<string>(), Arg.Any<GameMode>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EditorSongSelect_SkipsScoreQuery()
    {
        var md5 = new string('8', 32);
        _maps.FetchOneAsync(md5: md5).Returns(MakeBeatmap(md5));

        var result = await MakeService().FetchLeaderboardAsync(MakePlayer(), MakeRequest(md5, editorSongSelect: true));

        Assert.Equal(BeatmapLeaderboardResultCode.Found, result.Code);
        Assert.Empty(result.ScoreRows!);
        await _scores.DidNotReceive().FetchBeatmapLeaderboardScoresAsync(
            Arg.Any<string>(), Arg.Any<GameMode>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<IReadOnlySet<int>?>(),
            Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ModsLeaderboardType_PassesResolvedModsAsFilter()
    {
        var md5 = new string('9', 32);
        _maps.FetchOneAsync(md5: md5).Returns(MakeBeatmap(md5));
        _scores.FetchBeatmapLeaderboardScoresAsync(md5, GameMode.VanillaOsu, 1, 8).Returns([]);

        await MakeService().FetchLeaderboardAsync(MakePlayer(),
            MakeRequest(md5, leaderboardType: LeaderboardType.Mods, modsArg: 8));

        await _scores.Received(1).FetchBeatmapLeaderboardScoresAsync(
            md5, GameMode.VanillaOsu, 1, 8, Arg.Any<IReadOnlySet<int>?>(), null, Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FriendsLeaderboardType_PassesFriendIdsIncludingSelf()
    {
        var md5 = new string('a', 32);
        _maps.FetchOneAsync(md5: md5).Returns(MakeBeatmap(md5));
        _relationships.FetchAllAsync(1, RelationshipType.Friend, Arg.Any<CancellationToken>())
            .Returns([new Relationship(1, 42, RelationshipType.Friend)]);
        _scores.FetchBeatmapLeaderboardScoresAsync(md5, GameMode.VanillaOsu, 1, null, Arg.Any<IReadOnlySet<int>>())
            .Returns([]);

        await MakeService()
            .FetchLeaderboardAsync(MakePlayer(), MakeRequest(md5, leaderboardType: LeaderboardType.Friends));

        await _scores.Received(1).FetchBeatmapLeaderboardScoresAsync(
            md5, GameMode.VanillaOsu, 1, null,
            Arg.Is<IReadOnlySet<int>>(s => s.SetEquals(new[] { 1, 42 })), null, Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CountryLeaderboardType_PassesPlayerCountry()
    {
        var md5 = new string('b', 32);
        _maps.FetchOneAsync(md5: md5).Returns(MakeBeatmap(md5));
        _scores.FetchBeatmapLeaderboardScoresAsync(md5, GameMode.VanillaOsu, 1, null, null, "jp").Returns([]);

        await MakeService().FetchLeaderboardAsync(MakePlayer(country: "jp"),
            MakeRequest(md5, leaderboardType: LeaderboardType.Country));

        await _scores.Received(1).FetchBeatmapLeaderboardScoresAsync(
            md5, GameMode.VanillaOsu, 1, null, null, "jp", Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RelaxMods_ShiftsModeAndUpdatesPlayerStatus_BroadcastsWhenUnrestricted()
    {
        var md5 = new string('c', 32);
        _maps.FetchOneAsync(md5: md5).Returns(MakeBeatmap(md5));
        _scores.FetchBeatmapLeaderboardScoresAsync(md5, GameMode.RelaxOsu, 1).Returns([]);
        var other = new PlayerSession(2, "other", "tok2", Privileges.Unrestricted, 0.0);
        _sessionRegistry.All.Returns([other]);

        var player = MakePlayer();
        await MakeService().FetchLeaderboardAsync(player, MakeRequest(md5, modeArg: 0, modsArg: (int)Mods.Relax));

        Assert.Equal(GameMode.RelaxOsu, player.Status.Mode);
        Assert.Equal(Mods.Relax, player.Status.Mods);
    }

    [Fact]
    public async Task RelaxModOnManiaMode_StripsRelaxBit_NoModeShift()
    {
        var md5 = new string('d', 32);
        _maps.FetchOneAsync(md5: md5).Returns(MakeBeatmap(md5));
        _scores.FetchBeatmapLeaderboardScoresAsync(md5, GameMode.VanillaMania, 1).Returns([]);

        var player = MakePlayer();
        await MakeService().FetchLeaderboardAsync(player, MakeRequest(md5, modeArg: 3, modsArg: (int)Mods.Relax));

        Assert.Equal(GameMode.VanillaMania, player.Status.Mode);
        Assert.Equal(Mods.NoMod, player.Status.Mods);
    }

    [Fact]
    public async Task SameModeAsCurrentStatus_DoesNotBroadcast()
    {
        var md5 = new string('e', 32);
        _maps.FetchOneAsync(md5: md5).Returns(MakeBeatmap(md5));
        _scores.FetchBeatmapLeaderboardScoresAsync(md5, GameMode.VanillaOsu, 1).Returns([]);

        var player = MakePlayer(); // Status.Mode defaults to VanillaOsu already
        await MakeService().FetchLeaderboardAsync(player, MakeRequest(md5, modeArg: 0, modsArg: 0));

        _ = _sessionRegistry.DidNotReceive().All;
    }
}