using Bancho.Application.Abstractions;
using Dapper;
using MySqlConnector;
using Bancho.Application.Abstractions.Social;

namespace Bancho.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IRelationshipRepository" />
public sealed class MySqlRelationshipRepository(string connectionString) : IRelationshipRepository
{
    private sealed class RelationshipRow
    {
        public int User1 { get; set; }
        public int User2 { get; set; }
        public string Type { get; set; } = "";

        public Relationship ToRelationship() =>
            new(User1, User2, Type == "friend" ? RelationshipType.Friend : RelationshipType.Block);
    }

    private static string TypeColumn(RelationshipType type) => type == RelationshipType.Friend ? "friend" : "block";

    private MySqlConnection Connect() => new(connectionString);

    public async Task<Relationship> CreateAsync(int user1, int user2, RelationshipType type, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync(
            "INSERT INTO relationships (user1, user2, type) VALUES (@User1, @User2, @Type)",
            new { User1 = user1, User2 = user2, Type = TypeColumn(type) });

        return (await FetchOneAsync(user1, user2, cancellationToken))!;
    }

    public async Task<IReadOnlyList<Relationship>> FetchAllAsync(int user1, RelationshipType? type = null, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var sql = "SELECT user1, user2, type FROM relationships WHERE user1 = @User1";
        if (type is not null)
        {
            sql += " AND type = @Type";
        }

        var rows = await connection.QueryAsync<RelationshipRow>(
            sql,
            new { User1 = user1, Type = type is null ? null : TypeColumn(type.Value) });
        return rows.Select(r => r.ToRelationship()).ToList();
    }

    public async Task<Relationship?> FetchOneAsync(int user1, int user2, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var row = await connection.QuerySingleOrDefaultAsync<RelationshipRow>(
            "SELECT user1, user2, type FROM relationships WHERE user1 = @User1 AND user2 = @User2",
            new { User1 = user1, User2 = user2 });
        return row?.ToRelationship();
    }

    public async Task DeleteAsync(int user1, int user2, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync(
            "DELETE FROM relationships WHERE user1 = @User1 AND user2 = @User2",
            new { User1 = user1, User2 = user2 });
    }
}
