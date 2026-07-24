using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Scores;
using Basil.Application.Abstractions.Users;
using Basil.Application.Services.Authentication;
using Basil.Application.Services.Scores;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using NSubstitute;

namespace Basil.Application.Tests.UseCases.Scores;

public class ScoreSubmissionServiceTests
{
    private readonly IMapRepository _maps = Substitute.For<IMapRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly IReplayStorage _replayStorage = Substitute.For<IReplayStorage>();
    private readonly IScoreRepository _scores = Substitute.For<IScoreRepository>();
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();

    private ScoreSubmissionService MakeUseCase()
    {
        return new ScoreSubmissionService(
            _maps, _scores,
            new AuthenticationService(_sessionRegistry, _users, _passwordHasher),
            _replayStorage);
    }

    private static Beatmap MakeBeatmap()
    {
        var mapset = new Mapset(1, "a", "b", "d", DateTime.UtcNow, DateTime.UtcNow);
        return new Beatmap(
            new string('a', 32), 42, mapset, "c",
            "f.osu", TimeSpan.FromSeconds(1), 500, 0, 0, new Difficulty(GameMode.Standard, 1, 1, 1, 1, 1, 1), new Dictionary<string, int>());
    }

    private PlayerSession MakePlayer(int id = 7, string name = "cookiezi")
    {
        var session = new PlayerSession(id, name, "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
        _sessionRegistry.GetByName(name).Returns(session);
        _users.FetchPasswordHashAsync(id, Arg.Any<CancellationToken>()).Returns("hashed");
        _passwordHasher.Verify(Arg.Any<byte[]>(), "hashed").Returns(true);
        return session;
    }

    /// <summary>Puts the player in an active multiplayer round — the only state that satisfies the multiplayer-only gate.</summary>
    private static void PutInActiveRound(PlayerSession player, int roundId = 10)
    {
        var match = new MatchSession(0, "Grand Finals", "", "map", 1, new string('a', 32), player.Id,
            GameMode.Standard, Mods.NoMod, MatchWinCondition.Score, MatchTeamType.HeadToHead, false, 0, "#mp_0")
        {
            CurrentRoundId = roundId
        };
        player.Match = match;
    }

    /// <summary>16 score fields (after client_checksum/username stripping) — matches the Rijndael/FromSubmission fixture.</summary>
    private static string[] MakeScoreFields(
        string checksum = "chk", long score = 500_000, string grade = "S", bool passed = true, string mods = "0")
    {
        return
        [
            checksum, "300", "10", "5", "0", "0", "0", score.ToString(), "500", "False", grade, mods,
            passed ? "True" : "False", "0", "210520235959", "20210520 "
        ];
    }

    private static string[] MakeFullFields(string beatmapMd5, string username, string[] scoreFields)
    {
        return [beatmapMd5, username, .. scoreFields];
    }

    private static ScoreSubmissionRequest MakeRequest(
        string beatmapMd5, string username, string[] scoreFields, byte[]? replayData = null,
        string osuVersion = "20210520", string clientHash = "clienthash", string uniqueIds = "u1|u2")
    {
        return new ScoreSubmissionRequest(
            MakeFullFields(beatmapMd5, username, scoreFields),
            "pwmd5",
            osuVersion,
            clientHash,
            uniqueIds,
            null,
            beatmapMd5,
            60_000,
            30_000,
            replayData);
    }

    private void StubPersistence(long scoreId = 1L)
    {
        _scores.CreateAsync(Arg.Any<ScoreInsertRow>(), Arg.Any<CancellationToken>()).Returns(scoreId);
    }

    [Fact]
    public async Task BeatmapNotFound_ReturnsBeatmapNotFound()
    {
        _maps.FetchOneAsync(null, Arg.Any<string>(), null, null, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns((Beatmap?)null);

        var result = await MakeUseCase().SubmitAsync(MakeRequest(new string('a', 32), "cookiezi ", MakeScoreFields()));

        Assert.Equal(ScoreSubmissionResultCode.BeatmapNotFound, result.Code);
    }

    [Fact]
    public async Task PlayerNotOnline_ReturnsPlayerNotFound()
    {
        _maps.FetchOneAsync(null, Arg.Any<string>(), null, null, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(MakeBeatmap());
        _sessionRegistry.GetByName(Arg.Any<string>()).Returns((PlayerSession?)null);

        var result = await MakeUseCase().SubmitAsync(MakeRequest(new string('a', 32), "ghost ", MakeScoreFields()));

        Assert.Equal(ScoreSubmissionResultCode.PlayerNotFound, result.Code);
    }

    [Fact]
    public async Task NotInMatch_ReturnsNotInMultiplayer_NoPersist()
    {
        var bmap = MakeBeatmap();
        _maps.FetchOneAsync(null, bmap.Md5, null, null, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(bmap);
        MakePlayer(); // player.Match stays null — not in any room

        var result = await MakeUseCase().SubmitAsync(MakeRequest(bmap.Md5, "cookiezi ", MakeScoreFields()));

        Assert.Equal(ScoreSubmissionResultCode.NotInMultiplayer, result.Code);
        await _scores.DidNotReceive().CreateAsync(Arg.Any<ScoreInsertRow>(), Arg.Any<CancellationToken>());
        await _maps.DidNotReceive().IncrementPlayCountsAsync(Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await _replayStorage.DidNotReceive()
            .WriteAsync(Arg.Any<long>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InMatchButNoActiveRound_ReturnsNotInMultiplayer_NoPersist()
    {
        var bmap = MakeBeatmap();
        _maps.FetchOneAsync(null, bmap.Md5, null, null, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(bmap);
        var player = MakePlayer();
        player.Match = new MatchSession(0, "Lobby", "", "map", 1, new string('a', 32), player.Id,
            GameMode.Standard, Mods.NoMod, MatchWinCondition.Score, MatchTeamType.HeadToHead, false, 0, "#mp_0");
        // CurrentRoundId left null — room exists but no round has started (e.g. before !mp start).

        var result = await MakeUseCase().SubmitAsync(MakeRequest(bmap.Md5, "cookiezi ", MakeScoreFields()));

        Assert.Equal(ScoreSubmissionResultCode.NotInMultiplayer, result.Code);
        await _scores.DidNotReceive().CreateAsync(Arg.Any<ScoreInsertRow>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UsernameTrailingSupporterSpace_IsStrippedBeforeLookup()
    {
        var bmap = MakeBeatmap();
        _maps.FetchOneAsync(null, bmap.Md5, null, null, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(bmap);
        var player = MakePlayer(name: "cookiezi");
        PutInActiveRound(player);
        StubPersistence();

        await MakeUseCase().SubmitAsync(MakeRequest(bmap.Md5, "cookiezi ", MakeScoreFields()));

        _sessionRegistry.Received().GetByName("cookiezi");
    }

    [Fact]
    public async Task DuplicateChecksum_ReturnsDuplicateSubmission()
    {
        var bmap = MakeBeatmap();
        _maps.FetchOneAsync(null, bmap.Md5, null, null, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(bmap);
        var player = MakePlayer();
        PutInActiveRound(player);
        _scores.ExistsByOnlineChecksumAsync("chk", Arg.Any<CancellationToken>()).Returns(true);

        var result = await MakeUseCase().SubmitAsync(MakeRequest(bmap.Md5, "cookiezi ", MakeScoreFields()));

        Assert.Equal(ScoreSubmissionResultCode.DuplicateSubmission, result.Code);
        await _scores.DidNotReceive().CreateAsync(Arg.Any<ScoreInsertRow>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PassedRankedScore_AlwaysBestWithTopRank_Persists()
    {
        var bmap = MakeBeatmap();
        _maps.FetchOneAsync(null, bmap.Md5, null, null, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(bmap);
        var player = MakePlayer();
        PutInActiveRound(player);
        _scores.ExistsByOnlineChecksumAsync("chk", Arg.Any<CancellationToken>()).Returns(false);
        StubPersistence(999L);

        var result = await MakeUseCase()
            .SubmitAsync(MakeRequest(bmap.Md5, "cookiezi ", MakeScoreFields(score: 500_000, grade: "S")));

        Assert.Equal(ScoreSubmissionResultCode.Success, result.Code);
        Assert.Equal(999L, result.Result!.ScoreId);
        Assert.Equal(1, result.Result.Rank);

        await _scores.Received(1).CreateAsync(Arg.Any<ScoreInsertRow>(), Arg.Any<CancellationToken>());
        await _maps.Received(1).IncrementPlayCountsAsync(bmap.Id, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FailedScore_SkipsStatusPlacementAndUsesFailTime()
    {
        var bmap = MakeBeatmap();
        _maps.FetchOneAsync(null, bmap.Md5, null, null, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(bmap);
        var player = MakePlayer();
        PutInActiveRound(player);
        _scores.ExistsByOnlineChecksumAsync("chk", Arg.Any<CancellationToken>()).Returns(false);
        StubPersistence();

        var result =
            await MakeUseCase().SubmitAsync(MakeRequest(bmap.Md5, "cookiezi ", MakeScoreFields(passed: false)));

        Assert.Null(result.Result!.Rank);
        Assert.Equal(TimeSpan.FromMilliseconds(30_000), result.Result.Score.TimeElapsed);
    }

    [Fact]
    public async Task InvalidReplay_DiscardsReplayButStillPersistsScore()
    {
        var bmap = MakeBeatmap();
        _maps.FetchOneAsync(null, bmap.Md5, null, null, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(bmap);
        var player = MakePlayer();
        PutInActiveRound(player);
        _scores.ExistsByOnlineChecksumAsync("chk", Arg.Any<CancellationToken>()).Returns(false);
        StubPersistence();

        var result = await MakeUseCase().SubmitAsync(MakeRequest(bmap.Md5, "cookiezi ", MakeScoreFields(), [1, 2, 3]));

        Assert.Equal(ScoreSubmissionResultCode.Success, result.Code);
        await _replayStorage.DidNotReceive()
            .WriteAsync(Arg.Any<long>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ValidReplay_IsWrittenUnderNewScoreId()
    {
        var bmap = MakeBeatmap();
        _maps.FetchOneAsync(null, bmap.Md5, null, null, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(bmap);
        var player = MakePlayer();
        PutInActiveRound(player);
        _scores.ExistsByOnlineChecksumAsync("chk", Arg.Any<CancellationToken>()).Returns(false);
        StubPersistence(555L);
        var replayBytes = new byte[30];

        await MakeUseCase().SubmitAsync(MakeRequest(bmap.Md5, "cookiezi ", MakeScoreFields(), replayBytes));

        await _replayStorage.Received(1).WriteAsync(555L, replayBytes, Arg.Any<CancellationToken>());
    }
}
