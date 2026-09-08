using Basil.Server.Shared.Persistence;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Server.Tests.Features.Users;

/// <summary>
///     A fresh, fully migrated temp-file database, one per instance -- unlike
///     <see cref="Basil.Server.Tests.Shared.Persistence.SqliteFixture" />, which is shared for a
///     whole test class. A theory whose cases can collide on a unique
///     constraint (two different usernames producing the same generated <c>SafeName</c>) needs an
///     isolated database per case, not one shared across every <c>[InlineData]</c>.
/// </summary>
public sealed class UsersFixture : IAsyncDisposable
{
	private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"basil-users-test-{Guid.NewGuid():N}.db");

	private string ConnectionString => $"Data Source={_dbPath};Foreign Keys=True;Default Timeout=5";

	/// <inheritdoc />
	public ValueTask DisposeAsync()
	{
		// Release pooled connections before deleting, otherwise the file (and its WAL sidecars) can
		// still be locked on Windows.
		SqliteConnection.ClearAllPools();
		File.Delete(_dbPath);
		File.Delete(_dbPath + "-wal");
		File.Delete(_dbPath + "-shm");
		return ValueTask.CompletedTask;
	}

	/// <summary>Creates and migrates a fresh database.</summary>
	public static Task<UsersFixture> CreateAsync()
	{
		var fixture = new UsersFixture();
		SqlMigrationRunner.RunMigrations(fixture.ConnectionString);
		return Task.FromResult(fixture);
	}

	/// <summary>Inserts a user row with the given display name, omitting <c>SafeName</c> entirely.</summary>
	/// <param name="name">The display name to store.</param>
	/// <returns>The new row's id.</returns>
	public async Task<int> InsertUserAsync(string name)
	{
		await using var connection = SqliteConnectionFactory.Open(ConnectionString);
		return await connection.ExecuteScalarAsync<int>(
			"""
			INSERT INTO Users (Name, PwBcrypt) VALUES (@Name, 'unused');
			SELECT last_insert_rowid();
			""",
			new { Name = name });
	}

	/// <summary>Reads back the generated <c>SafeName</c> for a row.</summary>
	/// <param name="id">The id of the row to read.</param>
	/// <returns>The stored, database-generated <c>SafeName</c> value.</returns>
	public async Task<string> QuerySafeNameAsync(int id)
	{
		await using var connection = SqliteConnectionFactory.Open(ConnectionString);
		return await connection.QuerySingleAsync<string>(
			"SELECT SafeName FROM Users WHERE Id = @Id", new { Id = id });
	}
}