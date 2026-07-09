using Basil.Infrastructure.Persistence.Repositories;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Tests.Persistence;

/// <summary>Ported from app/repositories/ratings.py, scoped to the getscores average-rating read.</summary>
public class SqliteRatingRepositoryTests(SqliteFixture fixture) : IClassFixture<SqliteFixture>
{
    private readonly SqliteRatingRepository _repository = new(fixture.ConnectionString);

    private async Task InsertUserAsync(int id, string name)
    {
        await using var connection = new SqliteConnection(fixture.ConnectionString);
        await connection.ExecuteAsync(
            """
            INSERT INTO Users (Id, Name, SafeName, Email, PwBcrypt, Priv, Country, CreationTime, LatestActivity)
            VALUES (@Id, @Name, @Name, @Email, 'unused', 3, 'xx', unixepoch(), unixepoch())
            """,
            new { Id = id, Name = name, Email = $"{name}@test.local" });
    }

    private async Task InsertRatingAsync(int userId, string mapMd5, int rating)
    {
        await using var connection = new SqliteConnection(fixture.ConnectionString);
        await connection.ExecuteAsync(
            "INSERT INTO Ratings (UserId, MapMd5, Rating) VALUES (@UserId, @MapMd5, @Rating)",
            new { UserId = userId, MapMd5 = mapMd5, Rating = rating });
    }

    [Fact]
    public async Task FetchAverageRating_NoRatings_ReturnsZero()
    {
        var average = await _repository.FetchAverageRatingAsync(new string('f', 32));

        Assert.Equal(0.0, average);
    }

    [Fact]
    public async Task FetchAverageRating_MultipleRatings_ReturnsAverage()
    {
        var mapMd5 = new string('g', 32);
        await InsertUserAsync(201, "rater-one");
        await InsertUserAsync(202, "rater-two");
        await InsertRatingAsync(201, mapMd5, 8);
        await InsertRatingAsync(202, mapMd5, 4);

        var average = await _repository.FetchAverageRatingAsync(mapMd5);

        Assert.Equal(6.0, average);
    }
}