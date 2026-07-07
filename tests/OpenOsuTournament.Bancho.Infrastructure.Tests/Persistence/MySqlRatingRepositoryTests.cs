using Dapper;
using MySqlConnector;
using OpenOsuTournament.Bancho.Infrastructure.Persistence.Repositories;

namespace OpenOsuTournament.Bancho.Infrastructure.Tests.Persistence;

/// <summary>Ported from app/repositories/ratings.py, scoped to the getscores average-rating read.</summary>
public class MySqlRatingRepositoryTests : IClassFixture<MySqlFixture>
{
    private readonly MySqlFixture _fixture;
    private readonly MySqlRatingRepository _repository;

    public MySqlRatingRepositoryTests(MySqlFixture fixture)
    {
        _fixture = fixture;
        _repository = new MySqlRatingRepository(fixture.ConnectionString);
    }

    private async Task InsertUserAsync(int id, string name)
    {
        await using var connection = new MySqlConnection(_fixture.ConnectionString);
        await connection.ExecuteAsync(
            """
            INSERT INTO users (id, name, safe_name, email, pw_bcrypt, priv, country, creation_time, latest_activity)
            VALUES (@Id, @Name, @Name, @Email, 'unused', 3, 'xx', UNIX_TIMESTAMP(), UNIX_TIMESTAMP())
            """,
            new { Id = id, Name = name, Email = $"{name}@test.local" });
    }

    private async Task InsertRatingAsync(int userId, string mapMd5, int rating)
    {
        await using var connection = new MySqlConnection(_fixture.ConnectionString);
        await connection.ExecuteAsync(
            "INSERT INTO ratings (userid, map_md5, rating) VALUES (@UserId, @MapMd5, @Rating)",
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