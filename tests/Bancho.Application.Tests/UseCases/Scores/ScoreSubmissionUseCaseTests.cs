using Bancho.Application.Abstractions;
using Bancho.Application.Sessions;
using Bancho.Application.UseCases.Authentication;
using Bancho.Application.UseCases.Scores;
using Bancho.Domain;
using NSubstitute;

namespace Bancho.Application.Tests.UseCases.Scores;

public class ScoreSubmissionUseCaseTests
{
    private readonly IMapRepository _maps = Substitute.For<IMapRepository>();
    private readonly IScoreRepository _scores = Substitute.For<IScoreRepository>();
    private readonly IScoreSubmissionPersistence _persistence = Substitute.For<IScoreSubmissionPersistence>();
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly IReplayStorage _replayStorage = Substitute.For<IReplayStorage>();
    private readonly ILeaderboardStore _leaderboardStore = Substitute.For<ILeaderboardStore>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private ScoreSubmissionUseCase MakeUseCase() => new(
        _maps, _scores, _persistence,
        new BanchoAuthenticationService(_sessionRegistry, _users, _passwordHasher),
        _replayStorage, _leaderboardStore, _clock);

    private static Beatmap MakeBeatmap(RankedStatus status = RankedStatus.Ranked) => new(
        Md5: new string('a', 32), Id: 42, SetId: 1, Artist: "a", Title: "b", Version: "c", Creator: "d",
        LastUpdate: DateTime.UtcNow, TotalLength: 1, MaxCombo: 500, Status: status, Frozen: false,
        Plays: 0, Passes: 0, Mode: GameMode.VanillaOsu, Bpm: 1, Cs: 1, Od: 1, Ar: 1, Hp: 1, Diff: 1, Filename: "f.osu");

    private PlayerSession MakePlayer(int id = 7, string name = "cookiezi")
    {
        var session = new PlayerSession(id, name, "token", Privileges.Unrestricted, 0.0);
        _sessionRegistry.GetByName(name).Returns(session);
        _users.FetchPasswordHashAsync(id, Arg.Any<CancellationToken>()).Returns("hashed");
        _passwordHasher.Verify(Arg.Any<byte[]>(), "hashed").Returns(true);
        return session;
    }

    /// <summary>16 score fields (after client_checksum/username stripping) — matches the Rijndael/FromSubmission fixture.</summary>
    private static string[] MakeScoreFields(
        string checksum = "chk", long score = 500_000, string grade = "S", bool passed = true, string mods = "0") =>
    [
        checksum, "300", "10", "5", "0", "0", "0", score.ToString(), "500", "False", grade, mods,
        passed ? "True" : "False", "0", "210520235959", "20210520 ",
    ];

    private static string[] MakeFullFields(string beatmapMd5, string username, string[] scoreFields) =>
        [beatmapMd5, username, .. scoreFields];

    private ScoreSubmissionRequest MakeRequest(
        string beatmapMd5, string username, string[] scoreFields, byte[]? replayData = null,
        string osuVersion = "20210520", string clientHash = "clienthash", string uniqueIds = "u1|u2") => new(
        ScoreDataFields: MakeFullFields(beatmapMd5, username, scoreFields),
        PasswordMd5: "pwmd5",
        OsuVersion: osuVersion,
        ClientHash: clientHash,
        UniqueIds: uniqueIds,
        StoryboardMd5: null,
        UpdatedBeatmapHash: beatmapMd5,
        ScoreTime: 60_000,
        FailTime: 30_000,
        ReplayData: replayData);

    private void StubPersistence(long scoreId = 1L) =>
        _persistence.PersistScoreSubmissionAsync(
                Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<GameMode>(),
                Arg.Any<ScoreInsertRow>(), Arg.Any<StatsUpdateRow>(), Arg.Any<CancellationToken>())
            .Returns(scoreId);

    [Fact]
    public async Task BeatmapNotFound_ReturnsBeatmapNotFound()
    {
        _maps.FetchOneAsync(id: null, md5: Arg.Any<string>(), filename: null, setId: null, Arg.Any<CancellationToken>())
            .Returns((Beatmap?)null);

        var result = await MakeUseCase().SubmitAsync(MakeRequest(new string('a', 32), "cookiezi ", MakeScoreFields()));

        Assert.Equal(ScoreSubmissionResultCode.BeatmapNotFound, result.Code);
    }

    [Fact]
    public async Task PlayerNotOnline_ReturnsPlayerNotFound()
    {
        _maps.FetchOneAsync(id: null, md5: Arg.Any<string>(), filename: null, setId: null, Arg.Any<CancellationToken>())
            .Returns(MakeBeatmap());
        _sessionRegistry.GetByName(Arg.Any<string>()).Returns((PlayerSession?)null);

        var result = await MakeUseCase().SubmitAsync(MakeRequest(new string('a', 32), "ghost ", MakeScoreFields()));

        Assert.Equal(ScoreSubmissionResultCode.PlayerNotFound, result.Code);
    }

    [Fact]
    public async Task UsernameTrailingSupporterSpace_IsStrippedBeforeLookup()
    {
        var bmap = MakeBeatmap();
        _maps.FetchOneAsync(id: null, md5: bmap.Md5, filename: null, setId: null, Arg.Any<CancellationToken>()).Returns(bmap);
        MakePlayer(name: "cookiezi");
        StubPersistence();

        await MakeUseCase().SubmitAsync(MakeRequest(bmap.Md5, "cookiezi ", MakeScoreFields()));

        _sessionRegistry.Received().GetByName("cookiezi");
    }

    [Fact]
    public async Task DuplicateChecksum_ReturnsDuplicateSubmission()
    {
        var bmap = MakeBeatmap();
        _maps.FetchOneAsync(id: null, md5: bmap.Md5, filename: null, setId: null, Arg.Any<CancellationToken>()).Returns(bmap);
        MakePlayer();
        _scores.ExistsByOnlineChecksumAsync("chk", Arg.Any<CancellationToken>()).Returns(true);

        var result = await MakeUseCase().SubmitAsync(MakeRequest(bmap.Md5, "cookiezi ", MakeScoreFields()));

        Assert.Equal(ScoreSubmissionResultCode.DuplicateSubmission, result.Code);
        await _persistence.DidNotReceive().PersistScoreSubmissionAsync(
            Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<GameMode>(),
            Arg.Any<ScoreInsertRow>(), Arg.Any<StatsUpdateRow>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FirstScoreOnMap_PassedRankedBest_PersistsAndUpdatesRankAndStats()
    {
        var bmap = MakeBeatmap(RankedStatus.Ranked);
        _maps.FetchOneAsync(id: null, md5: bmap.Md5, filename: null, setId: null, Arg.Any<CancellationToken>()).Returns(bmap);
        var player = MakePlayer();
        player.ModeStats[(int)GameMode.VanillaOsu] = new CachedPlayerStats(1000, 500, 0, 1, 100, 200, 300, 0);
        _scores.ExistsByOnlineChecksumAsync("chk", Arg.Any<CancellationToken>()).Returns(false);
        _scores.FetchPersonalBestLeaderboardScoreAsync(bmap.Md5, GameMode.VanillaOsu, player.Id, Arg.Any<CancellationToken>())
            .Returns((PersonalBestLeaderboardScoreRow?)null);
        _scores.FetchPersonalBestLeaderboardRankAsync(bmap.Md5, GameMode.VanillaOsu, 500_000, Arg.Any<CancellationToken>()).Returns(1);
        StubPersistence(999L);
        _leaderboardStore.FetchGlobalRankAsync(player.Id, (int)GameMode.VanillaOsu, Arg.Any<CancellationToken>()).Returns(1);

        var result = await MakeUseCase().SubmitAsync(MakeRequest(bmap.Md5, "cookiezi ", MakeScoreFields(score: 500_000, grade: "S")));

        Assert.Equal(ScoreSubmissionResultCode.Success, result.Code);
        Assert.Equal(999L, result.Result!.ScoreId);
        Assert.Equal(SubmissionStatus.Best, result.Result.Score.Status);
        Assert.Equal(1, result.Result.Score.Rank);

        await _persistence.Received(1).PersistScoreSubmissionAsync(
            markPreviousBestSubmitted: true, mapMd5: bmap.Md5, userId: player.Id, mode: GameMode.VanillaOsu,
            scoreRow: Arg.Any<ScoreInsertRow>(),
            statsUpdate: Arg.Is<StatsUpdateRow>(s =>
                s.Tscore == 1000 + 500_000 && s.Rscore == 500 + 500_000 && s.Plays == 2 && s.Playtime == 100 + 60
                && s.MaxCombo == 500 && s.TotalHits == 300 + 315 && s.SCount == 1
                && s.XhCount == 0 && s.XCount == 0 && s.ShCount == 0 && s.ACount == 0),
            cancellationToken: Arg.Any<CancellationToken>());
        await _leaderboardStore.Received(1).AddToGlobalLeaderboardAsync(player.Id, (int)GameMode.VanillaOsu, 500 + 500_000, Arg.Any<CancellationToken>());
        await _maps.Received(1).IncrementPlayCountsAsync(bmap.Id, true, Arg.Any<CancellationToken>());
        Assert.Equal(500 + 500_000, player.ModeStats[(int)GameMode.VanillaOsu].Rscore);
    }

    [Fact]
    public async Task FailedScore_SkipsStatusPlacementAndUsesFailTime()
    {
        var bmap = MakeBeatmap();
        _maps.FetchOneAsync(id: null, md5: bmap.Md5, filename: null, setId: null, Arg.Any<CancellationToken>()).Returns(bmap);
        MakePlayer();
        _scores.ExistsByOnlineChecksumAsync("chk", Arg.Any<CancellationToken>()).Returns(false);
        StubPersistence();

        var result = await MakeUseCase().SubmitAsync(MakeRequest(bmap.Md5, "cookiezi ", MakeScoreFields(passed: false)));

        Assert.Equal(SubmissionStatus.Failed, result.Result!.Score.Status);
        Assert.Null(result.Result.Score.Rank);
        Assert.Equal(30_000, result.Result.Score.TimeElapsed);
        await _scores.DidNotReceive().FetchPersonalBestLeaderboardRankAsync(
            Arg.Any<string>(), Arg.Any<GameMode>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidReplay_DiscardsReplayButStillPersistsScore()
    {
        var bmap = MakeBeatmap();
        _maps.FetchOneAsync(id: null, md5: bmap.Md5, filename: null, setId: null, Arg.Any<CancellationToken>()).Returns(bmap);
        MakePlayer();
        _scores.ExistsByOnlineChecksumAsync("chk", Arg.Any<CancellationToken>()).Returns(false);
        StubPersistence();

        var result = await MakeUseCase().SubmitAsync(MakeRequest(bmap.Md5, "cookiezi ", MakeScoreFields(), replayData: [1, 2, 3]));

        Assert.Equal(ScoreSubmissionResultCode.Success, result.Code);
        await _replayStorage.DidNotReceive().WriteAsync(Arg.Any<long>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ValidReplay_IsWrittenUnderNewScoreId()
    {
        var bmap = MakeBeatmap();
        _maps.FetchOneAsync(id: null, md5: bmap.Md5, filename: null, setId: null, Arg.Any<CancellationToken>()).Returns(bmap);
        MakePlayer();
        _scores.ExistsByOnlineChecksumAsync("chk", Arg.Any<CancellationToken>()).Returns(false);
        StubPersistence(555L);
        var replayBytes = new byte[30];

        await MakeUseCase().SubmitAsync(MakeRequest(bmap.Md5, "cookiezi ", MakeScoreFields(), replayData: replayBytes));

        await _replayStorage.Received(1).WriteAsync(555L, replayBytes, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmittedNotBest_DoesNotMarkPreviousOrUpdateRank()
    {
        var bmap = MakeBeatmap(RankedStatus.Ranked);
        _maps.FetchOneAsync(id: null, md5: bmap.Md5, filename: null, setId: null, Arg.Any<CancellationToken>()).Returns(bmap);
        var player = MakePlayer();
        _scores.ExistsByOnlineChecksumAsync("chk", Arg.Any<CancellationToken>()).Returns(false);
        _scores.FetchPersonalBestLeaderboardScoreAsync(bmap.Md5, GameMode.VanillaOsu, player.Id, Arg.Any<CancellationToken>())
            .Returns(new PersonalBestLeaderboardScoreRow(1, 900_000, 500, 5, 10, 300, 0, 0, 0, false, 0, 123, "S"));
        StubPersistence(2L);

        var result = await MakeUseCase().SubmitAsync(MakeRequest(bmap.Md5, "cookiezi ", MakeScoreFields(score: 500_000)));

        Assert.Equal(SubmissionStatus.Submitted, result.Result!.Score.Status);
        await _persistence.Received(1).PersistScoreSubmissionAsync(
            markPreviousBestSubmitted: false, mapMd5: Arg.Any<string>(), userId: Arg.Any<int>(), mode: Arg.Any<GameMode>(),
            scoreRow: Arg.Any<ScoreInsertRow>(), statsUpdate: Arg.Any<StatsUpdateRow>(), cancellationToken: Arg.Any<CancellationToken>());
        await _leaderboardStore.DidNotReceive().AddToGlobalLeaderboardAsync(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<double>(), Arg.Any<CancellationToken>());
    }
}
