using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Persistence;

/// <summary>
///     Opens a SQLite connection with the per-connection PRAGMAs every repository needs: a real
///     <c>busy_timeout</c> (SQLite's own bounded-wait-with-backoff busy handler, not just the ADO.NET
///     command timeout that previously stood in for it) and <c>synchronous=NORMAL</c>, safe under
///     <c>journal_mode=WAL</c> (durable against process crashes; only a very recent commit can be lost
///     on an OS-level power failure, and the database file itself is never corrupted). Both are
///     per-connection session settings, not persisted like <c>journal_mode</c>, so they must be set on
///     every new connection — see ADR-001.
/// </summary>
internal static class SqliteConnectionFactory
{
	private const string PragmaSql = "PRAGMA busy_timeout = 5000; PRAGMA synchronous = NORMAL;";

	/// <summary>Opens a new connection against <paramref name="connectionString" /> with the standard PRAGMAs applied.</summary>
	public static SqliteConnection Open(string connectionString)
	{
		var connection = new SqliteConnection(connectionString);
		connection.Open();
		using var pragma = connection.CreateCommand();
		pragma.CommandText = PragmaSql;
		pragma.ExecuteNonQuery();
		return connection;
	}
}
