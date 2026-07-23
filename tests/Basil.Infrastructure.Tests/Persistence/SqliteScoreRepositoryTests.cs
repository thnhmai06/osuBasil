using Basil.Application.Abstractions.Scores;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Infrastructure.Persistence.Repositories;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Tests.Persistence;

public class SqliteScoreRepositoryTests(SqliteFixture fixture) : IClassFixture<SqliteFixture>
{
    private readonly SqliteScoreRepository _repository = new(fixture.ConnectionString);

    private async Task InsertUserAsync(int id, string name, string country = "xx", bool restricted = false)
    {
        var priv = restricted ? 0 : (int)UserPrivileges.Unrestricted;
        await using var connection = new SqliteConnection(fixture.ConnectionString);
        await connection.ExecuteAsync(
            """
            INSERT INTO Users (Id, Name, SafeName, PwBcrypt, Priv, Country)
            VALUES (@Id, @Name, @Name, 'unused', @Priv, @Country)
            """,
            new { Id = id, Name = name, Priv = priv, Country = country });
    }

    private async Task<long> InsertScoreAsync(
        string mapMd5, int userId, long score, Mods mods = Mods.NoMod,
        GameMode mode = GameMode.Standard, bool perfect = false, int? roundId = null, MatchTeam? team = null)
    {
        await using var connection = new SqliteConnection(fixture.ConnectionString);
        return await connection.ExecuteScalarAsync<long>(
            """
            INSERT INTO Scores (
                MapMd5, Score, Accuracy, MaxCombo, Mods, N300, N100, N50, NMiss, NGeki, NKatu,
                Grade, Mode, PlayTime, TimeElapsed, ClientFlags, UserId, Perfect, OnlineChecksum,
                SubmittedAt, RoundId, Team
            ) VALUES (
                @MapMd5, @Score, 95.0, 500, @Mods, 300, 10, 5, 0, 0, 0,
                'S', @Mode, datetime('now'), 120000, 0, @UserId, @Perfect, @Checksum,
                datetime('now'), @RoundId, @Team
            );
            SELECT last_insert_rowid();
            """,
            new
            {
                MapMd5 = mapMd5, Score = score, Mods = (int)mods, Mode = (int)mode,
                UserId = userId, Perfect = perfect, Checksum = Guid.NewGuid().ToString("N"),
                RoundId = roundId, Team = (int?)team
            });
    }

    private static ScoreInsertRow MakeInsertRow(string mapMd5, int userId, long score, string checksum)
    {
        return new ScoreInsertRow(
            mapMd5, score, 98.5, 500, Mods.NoMod,
            300, 10, 5, 0, 0, 0,
            "S", GameMode.Standard,
            DateTime.UtcNow, 120000, ClientFlags.Clean, userId,
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
    public async Task FetchFirstPlaceScore_ReturnsTopUnrestrictedScore()
    {
        var mapMd5 = new string('e', 32);
        await InsertUserAsync(319, "quinn");
        await InsertUserAsync(320, "restricted-rick", restricted: true);
        await InsertScoreAsync(mapMd5, 319, 700_000);
        await InsertScoreAsync(mapMd5, 320, 999_999);

        var firstPlace = await _repository.FetchFirstPlaceScoreAsync(mapMd5, GameMode.Standard);

        Assert.NotNull(firstPlace);
        Assert.Equal("quinn", firstPlace.Name);
    }

    [Fact]
    public async Task FetchFirstPlaceScore_NoScores_ReturnsNull()
    {
        var mapMd5 = new string('f', 32);
        Assert.Null(await _repository.FetchFirstPlaceScoreAsync(mapMd5, GameMode.Standard));
    }

    [Fact]
    public async Task FetchOwner_ReturnsUserIdAndMode()
    {
        var mapMd5 = new string('g', 32);
        await InsertUserAsync(321, "sybil");
        var scoreId = await InsertScoreAsync(mapMd5, 321, 500_000, mode: GameMode.Taiko);

        var owner = await _repository.FetchOwnerAsync(scoreId);

        Assert.NotNull(owner);
        Assert.Equal(321, owner.UserId);
        Assert.Equal(GameMode.Taiko, owner.Mode);
    }

    [Fact]
    public async Task FetchOwner_NotFound_ReturnsNull()
    {
        Assert.Null(await _repository.FetchOwnerAsync(999_999));
    }

    [Fact]
    public async Task FetchById_ReturnsFullRow()
    {
        var mapMd5 = new string('k', 32);
        await InsertUserAsync(322, "tara");
        var scoreId = await InsertScoreAsync(mapMd5, 322, 850_000, mode: GameMode.Mania);

        var row = await _repository.FetchByIdAsync(scoreId);

        Assert.NotNull(row);
        Assert.Equal(scoreId, row.Id);
        Assert.Equal(322, row.UserId);
        Assert.Equal(mapMd5, row.MapMd5);
        Assert.Equal(850_000, row.Score);
        Assert.Equal(GameMode.Mania, row.Mode);
        Assert.False(row.IsInvalidated);
    }

    [Fact]
    public async Task FetchById_AfterInvalidation_ReportsIsInvalidatedTrue()
    {
        var mapMd5 = new string('l', 32);
        await InsertUserAsync(323, "uma");
        var scoreId = await InsertScoreAsync(mapMd5, 323, 850_000);

        await _repository.InvalidateByMapMd5Async(mapMd5);
        var row = await _repository.FetchByIdAsync(scoreId);

        Assert.NotNull(row);
        Assert.True(row.IsInvalidated);
    }

    [Fact]
    public async Task FetchById_NotFound_ReturnsNull()
    {
        Assert.Null(await _repository.FetchByIdAsync(999_999));
    }

    [Fact]
    public async Task FetchByRoundId_ReturnsScoresOrderedByScoreDescending_WithUserNameJoined()
    {
        await InsertUserAsync(401, "roundwinner");
        await InsertUserAsync(402, "roundloser");
        var roundId = await InsertRoundAsync();
        var mapMd5 = new string('h', 32);
        await InsertScoreAsync(mapMd5, 401, 900_000, roundId: roundId, team: MatchTeam.Blue);
        await InsertScoreAsync(mapMd5, 402, 300_000, roundId: roundId, team: MatchTeam.Red);
        // Not linked to the round — must not show up.
        await InsertScoreAsync(mapMd5, 402, 999_999);

        var rows = await _repository.FetchByRoundIdAsync(roundId);

        Assert.Equal(2, rows.Count);
        Assert.Equal("roundwinner", rows[0].UserName);
        Assert.Equal(900_000, rows[0].Score);
        Assert.Equal(MatchTeam.Blue, rows[0].Team);
        Assert.Equal("roundloser", rows[1].UserName);
    }

    [Fact]
    public async Task InvalidateByMapMd5_FlagsMatchingScoresOnly()
    {
        var invalidatedMd5 = new string('i', 32);
        var otherMd5 = new string('j', 32);
        await InsertUserAsync(410, "invalidated-owner");
        var invalidatedId = await InsertScoreAsync(invalidatedMd5, 410, 600_000);
        var otherId = await InsertScoreAsync(otherMd5, 410, 600_000);

        await _repository.InvalidateByMapMd5Async(invalidatedMd5);

        Assert.True(await FetchIsInvalidatedAsync(invalidatedId));
        Assert.False(await FetchIsInvalidatedAsync(otherId));
    }

    private async Task<bool> FetchIsInvalidatedAsync(long scoreId)
    {
        await using var connection = new SqliteConnection(fixture.ConnectionString);
        return await connection.ExecuteScalarAsync<bool>("SELECT IsInvalidated FROM Scores WHERE Id = @Id",
            new { Id = scoreId });
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