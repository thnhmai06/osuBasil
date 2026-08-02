using Basil.Application.Abstractions.Social;
using Basil.Domain.Social;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IRelationshipRepository" />
/// <remarks>
///     The relationship kind is stored as the text <c>"friend"</c> or <c>"block"</c> in the Type
///     column and mapped back to <see cref="RelationshipType" /> when read. Rows map through the
///     private mutable <c>RelationshipRow</c> DTO, since Dapper fills by property name rather than
///     through a positional record constructor.
/// </remarks>
public sealed class SqliteRelationshipRepository(string connectionString, ILogger<SqliteRelationshipRepository> logger)
	: IRelationshipRepository
{
	/// <inheritdoc />
	/// <remarks>
	///     Inserts the row, then re-reads it through <see cref="FetchOneAsync" /> so the returned
	///     relationship reflects the persisted state.
	/// </remarks>
	public async Task<Relationship> CreateAsync(int user1, int user2, RelationshipType type,
		CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		await connection.ExecuteAsync(
			"INSERT INTO Relationships (User1, User2, Type) VALUES (@User1, @User2, @Type)",
			new { User1 = user1, User2 = user2, Type = TypeColumn(type) });
		logger.LogDebug("Relationship created: User1={User1} User2={User2} Type={Type}", user1, user2, type);

		return (await FetchOneAsync(user1, user2, cancellationToken))!;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Relationship>> FetchAllAsync(int user1, RelationshipType? type = null,
		CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var sql = "SELECT User1, User2, Type FROM Relationships WHERE User1 = @User1";
		if (type is not null) sql += " AND Type = @Type";

		var rows = await connection.QueryAsync<RelationshipRow>(
			sql,
			new { User1 = user1, Type = type is null ? null : TypeColumn(type.Value) });
		return [.. rows.Select(r => r.ToRelationship())];
	}

	/// <inheritdoc />
	public async Task<Relationship?> FetchOneAsync(int user1, int user2, CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var row = await connection.QuerySingleOrDefaultAsync<RelationshipRow>(
			"SELECT User1, User2, Type FROM Relationships WHERE User1 = @User1 AND User2 = @User2",
			new { User1 = user1, User2 = user2 });
		return row?.ToRelationship();
	}

	/// <inheritdoc />
	public async Task DeleteAsync(int user1, int user2, CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		await connection.ExecuteAsync(
			"DELETE FROM Relationships WHERE User1 = @User1 AND User2 = @User2",
			new { User1 = user1, User2 = user2 });
		logger.LogDebug("Relationship deleted: User1={User1} User2={User2}", user1, user2);
	}

	/// <summary>Maps a relationship kind to its stored text column value.</summary>
	/// <param name="type">The relationship kind to convert.</param>
	/// <returns>The stored column value.</returns>
	private static string TypeColumn(RelationshipType type)
	{
		return type == RelationshipType.Friend ? "friend" : "block";
	}

	/// <summary>Maps a stored text column value back to a relationship kind.</summary>
	/// <param name="column">The stored column value.</param>
	/// <returns>The relationship kind.</returns>
	private static RelationshipType TypeFromColumn(string column)
	{
		return column == "friend" ? RelationshipType.Friend : RelationshipType.Block;
	}

	/// <summary>Creates a new SQLite connection using the repository's connection string.</summary>
	private SqliteConnection Connect()
	{
		return new SqliteConnection(connectionString);
	}

	/// <summary>
	///     A mutable row DTO matching the Relationships table columns. Mutable because Dapper fills
	///     by property name, not through a positional record constructor.
	/// </summary>
	private sealed class RelationshipRow
	{
		public int User1 { get; set; }
		public int User2 { get; set; }
		public string Type { get; set; } = "";

		/// <summary>
		///     Builds a <see cref="Relationship" /> from this row, converting the stored column
		///     value.
		/// </summary>
		/// <returns>The domain relationship.</returns>
		public Relationship ToRelationship()
		{
			return new Relationship(User1, User2, TypeFromColumn(Type));
		}
	}
}