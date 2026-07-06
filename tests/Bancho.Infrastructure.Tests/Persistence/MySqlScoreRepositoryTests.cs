using Bancho.Application.Abstractions;
using Bancho.Domain;
using Bancho.Infrastructure.Persistence;
using Dapper;
using MySqlConnector;

namespace Bancho.Infrastructure.Tests.Persistence;

/// <summary>
/// Ported from app/repositories/scores.py's fetch_beatmap_leaderboard_scores/
/// fetch_personal_best_leaderboard_score/fetch_personal_best_leaderboard_rank, collapsed to
/// unconditional score-based ranking (no pp branch) per the no-pp scope decision.
/// </summary>
public class MySqlScoreRepositoryTests : IClassFixture<MySqlFixture>
{
    private readonly MySqlFixture _fixture;
    private readonly MySqlScoreRepository _repository;

    public MySqlScoreRepositoryTests(MySqlFixture fixture)
    {
        _fixture = fixture;
        _repository = new MySqlScoreRepository(fixture.ConnectionString);
    }

    private async Task InsertUserAsync(int id, string name, string country = "xx", bool restricted = false)
    {
        var priv = restricted ? 0 : (int)Privileges.Unrestricted;
        await using var connection = new MySqlConnection(_fixture.ConnectionString);
        await connection.ExecuteAsync(
            """
            INSERT INTO users (id, name, safe_name, email, pw_bcrypt, priv, country, creation_time, latest_activity)
            VALUES (@Id, @Name, @Name, @Email, 'unused', @Priv, @Country, UNIX_TIMESTAMP(), UNIX_TIMESTAMP())
            """,
            new { Id = id, Name = name, Email = $"{name}@test.local", Priv = priv, Country = country });
    }

    private async Task<long> InsertScoreAsync(
        string mapMd5, int userId, long score, int mods = 0, SubmissionStatus status = SubmissionStatus.Best,
        GameMode mode = GameMode.VanillaOsu, bool perfect = false)
    {
        await using var connection = new MySqlConnection(_fixture.ConnectionString);
        return await connection.ExecuteScalarAsync<long>(
            """
            INSERT INTO scores (
                map_md5, score, pp, acc, max_combo, mods, n300, n100, n50, nmiss, ngeki, nkatu,
                grade, status, mode, play_time, time_elapsed, client_flags, userid, perfect, online_checksum
            ) VALUES (
                @MapMd5, @Score, 0, 95.0, 500, @Mods, 300, 10, 5, 0, 0, 0,
                'S', @Status, @Mode, NOW(), 120000, 0, @UserId, @Perfect, @Checksum
            );
            SELECT LAST_INSERT_ID();
            """,
            new
            {
                MapMd5 = mapMd5, Score = score, Mods = mods, Status = (int)status, Mode = (int)mode,
                UserId = userId, Perfect = perfect, Checksum = Guid.NewGuid().ToString("N"),
            });
    }

    [Fact]
    public async Task FetchBeatmapLeaderboardScores_OrdersByScoreDescending_UnrestrictedOnly()
    {
        var mapMd5 = new string('1', 32);
        await InsertUserAsync(301, "alice");
        await InsertUserAsync(302, "bob");
        await InsertUserAsync(303, "restricted-carol", restricted: true);
        await InsertScoreAsync(mapMd5, 301, 500_000);
        await InsertScoreAsync(mapMd5, 302, 900_000);
        await InsertScoreAsync(mapMd5, 303, 999_999);

        var rows = await _repository.FetchBeatmapLeaderboardScoresAsync(mapMd5, GameMode.VanillaOsu, userId: 999);

        Assert.Equal(2, rows.Count);
        Assert.Equal("bob", rows[0].Name);
        Assert.Equal(900_000, rows[0].Score);
        Assert.Equal("alice", rows[1].Name);
    }

    [Fact]
    public async Task FetchBeatmapLeaderboardScores_IncludesOwnRestrictedRow()
    {
        var mapMd5 = new string('2', 32);
        await InsertUserAsync(304, "restricted-self", restricted: true);
        await InsertScoreAsync(mapMd5, 304, 100_000);

        var rows = await _repository.FetchBeatmapLeaderboardScoresAsync(mapMd5, GameMode.VanillaOsu, userId: 304);

        Assert.Single(rows);
        Assert.Equal("restricted-self", rows[0].Name);
    }

    [Fact]
    public async Task FetchBeatmapLeaderboardScores_FiltersByMode()
    {
        var mapMd5 = new string('3', 32);
        await InsertUserAsync(305, "dave");
        await InsertScoreAsync(mapMd5, 305, 500_000, mode: GameMode.VanillaOsu);
        await InsertScoreAsync(mapMd5, 305, 600_000, mode: GameMode.VanillaTaiko);

        var rows = await _repository.FetchBeatmapLeaderboardScoresAsync(mapMd5, GameMode.VanillaTaiko, userId: 305);

        Assert.Single(rows);
        Assert.Equal(600_000, rows[0].Score);
    }

    [Fact]
    public async Task FetchBeatmapLeaderboardScores_ExcludesNonBestStatus()
    {
        var mapMd5 = new string('4', 32);
        await InsertUserAsync(306, "erin");
        await InsertScoreAsync(mapMd5, 306, 700_000, status: SubmissionStatus.Submitted);

        var rows = await _repository.FetchBeatmapLeaderboardScoresAsync(mapMd5, GameMode.VanillaOsu, userId: 306);

        Assert.Empty(rows);
    }

    [Fact]
    public async Task FetchBeatmapLeaderboardScores_ModsFilter_OnlyMatchingMods()
    {
        var mapMd5 = new string('5', 32);
        await InsertUserAsync(307, "frank");
        await InsertScoreAsync(mapMd5, 307, 500_000, mods: 8); // HD
        await InsertScoreAsync(mapMd5, 307, 600_000, mods: 64); // DT

        var rows = await _repository.FetchBeatmapLeaderboardScoresAsync(mapMd5, GameMode.VanillaOsu, userId: 307, mods: 8);

        Assert.Single(rows);
        Assert.Equal(500_000, rows[0].Score);
    }

    [Fact]
    public async Task FetchBeatmapLeaderboardScores_FriendsFilter_OnlyMatchingUserIds()
    {
        var mapMd5 = new string('6', 32);
        await InsertUserAsync(308, "grace");
        await InsertUserAsync(309, "heidi");
        await InsertScoreAsync(mapMd5, 308, 500_000);
        await InsertScoreAsync(mapMd5, 309, 600_000);

        var rows = await _repository.FetchBeatmapLeaderboardScoresAsync(
            mapMd5, GameMode.VanillaOsu, userId: 308, friendIds: new HashSet<int> { 308 });

        Assert.Single(rows);
        Assert.Equal("grace", rows[0].Name);
    }

    [Fact]
    public async Task FetchBeatmapLeaderboardScores_CountryFilter_OnlyMatchingCountry()
    {
        var mapMd5 = new string('7', 32);
        await InsertUserAsync(310, "ivan", country: "jp");
        await InsertUserAsync(311, "judy", country: "us");
        await InsertScoreAsync(mapMd5, 310, 500_000);
        await InsertScoreAsync(mapMd5, 311, 600_000);

        var rows = await _repository.FetchBeatmapLeaderboardScoresAsync(
            mapMd5, GameMode.VanillaOsu, userId: 310, country: "jp");

        Assert.Single(rows);
        Assert.Equal("ivan", rows[0].Name);
    }

    [Fact]
    public async Task FetchPersonalBestLeaderboardScore_ReturnsHighestOwnScore()
    {
        var mapMd5 = new string('8', 32);
        await InsertUserAsync(312, "kevin");
        await InsertScoreAsync(mapMd5, 312, 500_000, status: SubmissionStatus.Submitted);
        await InsertScoreAsync(mapMd5, 312, 900_000, status: SubmissionStatus.Best);

        var best = await _repository.FetchPersonalBestLeaderboardScoreAsync(mapMd5, GameMode.VanillaOsu, 312);

        Assert.NotNull(best);
        Assert.Equal(900_000, best!.Score);
        Assert.Equal("S", best.Grade);
    }

    [Fact]
    public async Task FetchPersonalBestLeaderboardScore_NoScore_ReturnsNull()
    {
        var mapMd5 = new string('9', 32);
        Assert.Null(await _repository.FetchPersonalBestLeaderboardScoreAsync(mapMd5, GameMode.VanillaOsu, 999));
    }

    [Fact]
    public async Task FetchPersonalBestLeaderboardRank_CountsUnrestrictedScoresAbove_PlusOne()
    {
        var mapMd5 = new string('a', 32);
        await InsertUserAsync(313, "laura");
        await InsertUserAsync(314, "mallory");
        await InsertUserAsync(315, "restricted-nathan", restricted: true);
        await InsertScoreAsync(mapMd5, 313, 900_000);
        await InsertScoreAsync(mapMd5, 314, 950_000);
        await InsertScoreAsync(mapMd5, 315, 999_999); // restricted, doesn't count

        var rank = await _repository.FetchPersonalBestLeaderboardRankAsync(mapMd5, GameMode.VanillaOsu, 800_000);

        Assert.Equal(3, rank); // laura + mallory above, +1
    }

    private static ScoreInsertRow MakeInsertRow(string mapMd5, int userId, long score, string checksum) => new(
        MapMd5: mapMd5, Score: score, Acc: 98.5, MaxCombo: 500, Mods: 0,
        N300: 300, N100: 10, N50: 5, NMiss: 0, NGeki: 0, NKatu: 0,
        Grade: "S", Status: (int)SubmissionStatus.Best, Mode: (int)GameMode.VanillaOsu,
        PlayTime: DateTime.UtcNow, TimeElapsed: 120000, ClientFlags: 0, UserId: userId,
        Perfect: false, OnlineChecksum: checksum);

    [Fact]
    public async Task Create_InsertsRowAndReturnsGeneratedId()
    {
        var mapMd5 = new string('b', 32);
        await InsertUserAsync(316, "nina");

        var id = await _repository.CreateAsync(MakeInsertRow(mapMd5, 316, 750_000, Guid.NewGuid().ToString("N")));

        Assert.True(id > 0);
        Assert.True(await _repository.ExistsByOnlineChecksumAsync((await FetchChecksumAsync(id))!));
    }

    private async Task<string?> FetchChecksumAsync(long scoreId)
    {
        await using var connection = new MySqlConnection(_fixture.ConnectionString);
        return await connection.ExecuteScalarAsync<string>("SELECT online_checksum FROM scores WHERE id = @Id", new { Id = scoreId });
    }

    [Fact]
    public async Task ExistsByOnlineChecksum_NotFound_ReturnsFalse()
    {
        Assert.False(await _repository.ExistsByOnlineChecksumAsync(Guid.NewGuid().ToString("N")));
    }

    [Fact]
    public async Task ExistsByOnlineChecksum_Found_ReturnsTrue()
    {
        var mapMd5 = new string('c', 32);
        await InsertUserAsync(317, "olivia");
        var checksum = Guid.NewGuid().ToString("N");
        await InsertScoreAsync(mapMd5, 317, 500_000);
        await _repository.CreateAsync(MakeInsertRow(mapMd5, 317, 500_000, checksum));

        Assert.True(await _repository.ExistsByOnlineChecksumAsync(checksum));
    }

    [Fact]
    public async Task MarkPreviousBestScoresSubmitted_DemotesOnlyMatchingBestRow()
    {
        var mapMd5 = new string('d', 32);
        await InsertUserAsync(318, "peter");
        var scoreId = await InsertScoreAsync(mapMd5, 318, 500_000, status: SubmissionStatus.Best);

        await _repository.MarkPreviousBestScoresSubmittedAsync(mapMd5, 318, GameMode.VanillaOsu);

        await using var connection = new MySqlConnection(_fixture.ConnectionString);
        var status = await connection.ExecuteScalarAsync<int>("SELECT status FROM scores WHERE id = @Id", new { Id = scoreId });
        Assert.Equal((int)SubmissionStatus.Submitted, status);
    }

    [Fact]
    public async Task FetchFirstPlaceScore_ReturnsTopUnrestrictedScore()
    {
        var mapMd5 = new string('e', 32);
        await InsertUserAsync(319, "quinn");
        await InsertUserAsync(320, "restricted-rick", restricted: true);
        await InsertScoreAsync(mapMd5, 319, 700_000);
        await InsertScoreAsync(mapMd5, 320, 999_999);

        var firstPlace = await _repository.FetchFirstPlaceScoreAsync(mapMd5, GameMode.VanillaOsu);

        Assert.NotNull(firstPlace);
        Assert.Equal("quinn", firstPlace!.Name);
    }

    [Fact]
    public async Task FetchFirstPlaceScore_NoScores_ReturnsNull()
    {
        var mapMd5 = new string('f', 32);
        Assert.Null(await _repository.FetchFirstPlaceScoreAsync(mapMd5, GameMode.VanillaOsu));
    }

    [Fact]
    public async Task FetchOwner_ReturnsUserIdAndMode()
    {
        var mapMd5 = new string('g', 32);
        await InsertUserAsync(321, "sybil");
        var scoreId = await InsertScoreAsync(mapMd5, 321, 500_000, mode: GameMode.VanillaTaiko);

        var owner = await _repository.FetchOwnerAsync(scoreId);

        Assert.NotNull(owner);
        Assert.Equal(321, owner!.UserId);
        Assert.Equal(GameMode.VanillaTaiko, owner.Mode);
    }

    [Fact]
    public async Task FetchOwner_NotFound_ReturnsNull()
    {
        Assert.Null(await _repository.FetchOwnerAsync(999_999));
    }
}
