using Basil.Application.Abstractions.Scores;
using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Infrastructure.Persistence.Repositories;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Tests.Persistence;

/// <summary>
///     Ported from app/repositories/scores.py's fetch_beatmap_leaderboard_scores/
///     fetch_personal_best_leaderboard_score/fetch_personal_best_leaderboard_rank, collapsed to
///     unconditional score-based ranking (no pp branch) per the no-pp scope decision.
/// </summary>
public class SqliteScoreRepositoryTests(SqliteFixture fixture) : IClassFixture<SqliteFixture>
{
    private readonly SqliteScoreRepository _repository = new(fixture.ConnectionString);

    private async Task InsertUserAsync(int id, string name, string country = "xx", bool restricted = false)
    {
        var priv = restricted ? 0 : (int)Privileges.Unrestricted;
        await using var connection = new SqliteConnection(fixture.ConnectionString);
        await connection.ExecuteAsync(
            """
            INSERT INTO Users (Id, Name, SafeName, Email, PwBcrypt, Priv, Country, CreationTime, LatestActivity)
            VALUES (@Id, @Name, @Name, @Email, 'unused', @Priv, @Country, unixepoch(), unixepoch())
            """,
            new { Id = id, Name = name, Email = $"{name}@test.local", Priv = priv, Country = country });
    }

    private async Task<long> InsertScoreAsync(
        string mapMd5, int userId, long score, int mods = 0, SubmissionStatus status = SubmissionStatus.Best,
        GameMode mode = GameMode.VanillaOsu, bool perfect = false, int? roundId = null, int? team = null)
    {
        await using var connection = new SqliteConnection(fixture.ConnectionString);
        return await connection.ExecuteScalarAsync<long>(
            """
            INSERT INTO Scores (
                MapMd5, Score, Acc, MaxCombo, Mods, N300, N100, N50, NMiss, NGeki, NKatu,
                Grade, Status, Mode, PlayTime, TimeElapsed, ClientFlags, UserId, Perfect, OnlineChecksum,
                SubmittedAt, RoundId, Team
            ) VALUES (
                @MapMd5, @Score, 95.0, 500, @Mods, 300, 10, 5, 0, 0, 0,
                'S', @Status, @Mode, datetime('now'), 120000, 0, @UserId, @Perfect, @Checksum,
                datetime('now'), @RoundId, @Team
            );
            SELECT last_insert_rowid();
            """,
            new
            {
                MapMd5 = mapMd5, Score = score, Mods = mods, Status = (int)status, Mode = (int)mode,
                UserId = userId, Perfect = perfect, Checksum = Guid.NewGuid().ToString("N"),
                RoundId = roundId, Team = team
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

        var rows = await _repository.FetchBeatmapLeaderboardScoresAsync(mapMd5, GameMode.VanillaOsu, 999);

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

        var rows = await _repository.FetchBeatmapLeaderboardScoresAsync(mapMd5, GameMode.VanillaOsu, 304);

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

        var rows = await _repository.FetchBeatmapLeaderboardScoresAsync(mapMd5, GameMode.VanillaTaiko, 305);

        Assert.Single(rows);
        Assert.Equal(600_000, rows[0].Score);
    }

    [Fact]
    public async Task FetchBeatmapLeaderboardScores_ExcludesNonBestStatus()
    {
        var mapMd5 = new string('4', 32);
        await InsertUserAsync(306, "erin");
        await InsertScoreAsync(mapMd5, 306, 700_000, status: SubmissionStatus.Submitted);

        var rows = await _repository.FetchBeatmapLeaderboardScoresAsync(mapMd5, GameMode.VanillaOsu, 306);

        Assert.Empty(rows);
    }

    [Fact]
    public async Task FetchBeatmapLeaderboardScores_ModsFilter_OnlyMatchingMods()
    {
        var mapMd5 = new string('5', 32);
        await InsertUserAsync(307, "frank");
        await InsertScoreAsync(mapMd5, 307, 500_000, 8); // HD
        await InsertScoreAsync(mapMd5, 307, 600_000, 64); // DT

        var rows = await _repository.FetchBeatmapLeaderboardScoresAsync(mapMd5, GameMode.VanillaOsu, 307, 8);

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
            mapMd5, GameMode.VanillaOsu, 308, friendIds: new HashSet<int> { 308 });

        Assert.Single(rows);
        Assert.Equal("grace", rows[0].Name);
    }

    [Fact]
    public async Task FetchBeatmapLeaderboardScores_CountryFilter_OnlyMatchingCountry()
    {
        var mapMd5 = new string('7', 32);
        await InsertUserAsync(310, "ivan", "jp");
        await InsertUserAsync(311, "judy", "us");
        await InsertScoreAsync(mapMd5, 310, 500_000);
        await InsertScoreAsync(mapMd5, 311, 600_000);

        var rows = await _repository.FetchBeatmapLeaderboardScoresAsync(
            mapMd5, GameMode.VanillaOsu, 310, country: "jp");

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
        Assert.Equal(900_000, best.Score);
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

    private static ScoreInsertRow MakeInsertRow(string mapMd5, int userId, long score, string checksum)
    {
        return new ScoreInsertRow(
            mapMd5, score, 98.5, 500, 0,
            300, 10, 5, 0, 0, 0,
            "S", (int)SubmissionStatus.Best, (int)GameMode.VanillaOsu,
            DateTime.UtcNow, 120000, 0, userId,
            false, checksum, DateTime.UtcNow);
    }

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
        await using var connection = new SqliteConnection(fixture.ConnectionString);
        return await connection.ExecuteScalarAsync<string>("SELECT OnlineChecksum FROM Scores WHERE Id = @Id",
            new { Id = scoreId });
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

        await using var connection = new SqliteConnection(fixture.ConnectionString);
        var status =
            await connection.ExecuteScalarAsync<int>("SELECT Status FROM Scores WHERE Id = @Id", new { Id = scoreId });
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
        Assert.Equal("quinn", firstPlace.Name);
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
        Assert.Equal(321, owner.UserId);
        Assert.Equal(GameMode.VanillaTaiko, owner.Mode);
    }

    [Fact]
    public async Task FetchOwner_NotFound_ReturnsNull()
    {
        Assert.Null(await _repository.FetchOwnerAsync(999_999));
    }

    [Fact]
    public async Task FetchByRoundId_ReturnsScoresOrderedByScoreDescending_WithUserNameJoined()
    {
        await InsertUserAsync(401, "roundwinner");
        await InsertUserAsync(402, "roundloser");
        var roundId = await InsertRoundAsync();
        var mapMd5 = new string('h', 32);
        await InsertScoreAsync(mapMd5, 401, 900_000, roundId: roundId, team: 1);
        await InsertScoreAsync(mapMd5, 402, 300_000, roundId: roundId, team: 2);
        // Not linked to the round — must not show up.
        await InsertScoreAsync(mapMd5, 402, 999_999);

        var rows = await _repository.FetchByRoundIdAsync(roundId);

        Assert.Equal(2, rows.Count);
        Assert.Equal("roundwinner", rows[0].UserName);
        Assert.Equal(900_000, rows[0].Score);
        Assert.Equal(1, rows[0].Team);
        Assert.Equal("roundloser", rows[1].UserName);
    }

    private async Task<int> InsertRoundAsync()
    {
        await using var connection = new SqliteConnection(fixture.ConnectionString);
        var matchId = await connection.ExecuteScalarAsync<int>(
            """
            INSERT INTO Matches (Name, CreatedAt)
            VALUES ('test match', datetime('now'));
            SELECT last_insert_rowid();
            """);
        return await connection.ExecuteScalarAsync<int>(
            """
            INSERT INTO Rounds (MatchId, RoundIndex, BeatmapId, MapMd5, Mode, WinCondition, TeamType, BeatmapArtist, BeatmapTitle, BeatmapVersion, BeatmapCreator, Mods, StartedAt)
            VALUES (@MatchId, 1, 1, @MapMd5, 0, 0, 0, '', '', '', '', 0, datetime('now'));
            SELECT last_insert_rowid();
            """,
            new { MatchId = matchId, MapMd5 = new string('a', 32) });
    }
}