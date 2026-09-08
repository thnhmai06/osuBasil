using Basil.Server.Shared.Persistence;
using Microsoft.Data.Sqlite;

namespace Basil.Server.Tests.Shared.Persistence;

/// <summary>Runs migrations against a temp SQLite file, shared per test class; deletes the file on dispose.</summary>
public sealed class SqliteFixture : IAsyncLifetime
{
	private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"basil-test-{Guid.NewGuid():N}.db");

	public string ConnectionString => $"Data Source={_dbPath};Foreign Keys=True;Default Timeout=5";

	public ValueTask InitializeAsync()
	{
		SqlMigrationRunner.RunMigrations(ConnectionString);
		return ValueTask.CompletedTask;
	}

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
}