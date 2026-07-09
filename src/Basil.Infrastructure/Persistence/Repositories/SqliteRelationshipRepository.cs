using Basil.Application.Abstractions.Social;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IRelationshipRepository" />
public sealed class SqliteRelationshipRepository(string connectionString) : IRelationshipRepository
{
    public async Task<Relationship> CreateAsync(int user1, int user2, RelationshipType type,
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync(
            "INSERT INTO Relationships (User1, User2, Type) VALUES (@User1, @User2, @Type)",
            new { User1 = user1, User2 = user2, Type = TypeColumn(type) });

        return (await FetchOneAsync(user1, user2, cancellationToken))!;
    }

    public async Task<IReadOnlyList<Relationship>> FetchAllAsync(int user1, RelationshipType? type = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var sql = "SELECT User1, User2, Type FROM Relationships WHERE User1 = @User1";
        if (type is not null) sql += " AND Type = @Type";

        var rows = await connection.QueryAsync<RelationshipRow>(
            sql,
            new { User1 = user1, Type = type is null ? null : TypeColumn(type.Value) });
        return rows.Select(r => r.ToRelationship()).ToList();
    }

    public async Task<Relationship?> FetchOneAsync(int user1, int user2, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var row = await connection.QuerySingleOrDefaultAsync<RelationshipRow>(
            "SELECT User1, User2, Type FROM Relationships WHERE User1 = @User1 AND User2 = @User2",
            new { User1 = user1, User2 = user2 });
        return row?.ToRelationship();
    }

    public async Task DeleteAsync(int user1, int user2, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync(
            "DELETE FROM Relationships WHERE User1 = @User1 AND User2 = @User2",
            new { User1 = user1, User2 = user2 });
    }

    private static string TypeColumn(RelationshipType type)
    {
        return type == RelationshipType.Friend ? "friend" : "block";
    }

    private static RelationshipType TypeFromColumn(string column)
    {
        return column == "friend" ? RelationshipType.Friend : RelationshipType.Block;
    }

    private SqliteConnection Connect()
    {
        return new SqliteConnection(connectionString);
    }

    private sealed class RelationshipRow
    {
        public int User1 { get; set; }
        public int User2 { get; set; }
        public string Type { get; set; } = "";

        public Relationship ToRelationship()
        {
            return new Relationship(User1, User2, TypeFromColumn(Type));
        }
    }
}
