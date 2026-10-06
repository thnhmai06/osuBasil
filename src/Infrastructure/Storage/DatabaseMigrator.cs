using System.Globalization;
using System.Reflection;
using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Storage;

/// <summary>Brings the database up to date with the schema the server was built against.</summary>
/// <remarks>
///     Applies every pending migration script once, in version order, and records the applied version in the database.
/// </remarks>
internal sealed class DatabaseMigrator(Database database)
{
	private const string Marker = ".Migrations.";
	private const string Extension = ".sql";

	/// <summary>Applies every migration the database has not seen yet.</summary>
	/// <param name="cancellationToken">A token that cancels the migration.</param>
	public async Task MigrateAsync(CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken);

		var version = await ReadVersionAsync(connection, cancellationToken);

		foreach (var (next, sql) in Migrations())
		{
			if (next <= version)
				continue;

			await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
			await ExecuteAsync(connection, sql, cancellationToken, transaction);
			await ExecuteAsync(connection, $"PRAGMA user_version = {next};", cancellationToken, transaction);
			await transaction.CommitAsync(cancellationToken);
		}
	}

	private static IEnumerable<(int Version, string Sql)> Migrations()
	{
		var assembly = Assembly.GetExecutingAssembly();

		return assembly.GetManifestResourceNames()
			.Where(name => name.Contains(Marker, StringComparison.Ordinal)
			               && name.EndsWith(Extension, StringComparison.Ordinal))
			.Select(name => (Version: ParseVersion(name), Sql: ReadSql(assembly, name)))
			.OrderBy(migration => migration.Version)
			.ToList();
	}

	private static int ParseVersion(string resourceName)
	{
		var tail = resourceName[(resourceName.LastIndexOf(Marker, StringComparison.Ordinal) + Marker.Length)..];
		var separator = tail.IndexOf('_', StringComparison.Ordinal);
		var number = separator < 0 ? tail[..^Extension.Length] : tail[..separator];
		return int.Parse(number, CultureInfo.InvariantCulture);
	}

	private static string ReadSql(Assembly assembly, string resourceName)
	{
		using var stream = assembly.GetManifestResourceStream(resourceName)!;
		using var reader = new StreamReader(stream);
		return reader.ReadToEnd();
	}

	private static async Task<int> ReadVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
	{
		await using var command = connection.CreateCommand();
		command.CommandText = "PRAGMA user_version;";
		var value = await command.ExecuteScalarAsync(cancellationToken);
		return Convert.ToInt32(value, CultureInfo.InvariantCulture);
	}

	private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken,
		DbTransaction? transaction = null)
	{
		await using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.Transaction = (SqliteTransaction?)transaction;
		await command.ExecuteNonQueryAsync(cancellationToken);
	}
}
