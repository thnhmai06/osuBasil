using System.Globalization;
using System.Reflection;
using Npgsql;

namespace Basil.Infrastructure.Storage;

/// <summary>Brings the database up to date with the schema the server was built against.</summary>
/// <remarks>
///     Applies every pending migration script once, in version order, and records the applied version in the database.
///     The pending migrations are applied together: when one fails, the database is left as it was.
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
		await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

		await using (var createVersion = new NpgsqlCommand(
			             "create table if not exists schema_version (id integer primary key check (id = 1), version integer not null)",
			             connection, transaction))
		{
			await createVersion.ExecuteNonQueryAsync(cancellationToken);
		}

		var version = await ReadVersionAsync(connection, transaction, cancellationToken);
		var lastApplied = 0;

		foreach (var (next, sql) in Migrations())
		{
			if (next <= version)
				continue;

			await using var command = new NpgsqlCommand(sql, connection, transaction);
			await command.ExecuteNonQueryAsync(cancellationToken);
			lastApplied = next;
		}

		if (lastApplied > 0)
		{
			await using var upsert = new NpgsqlCommand(
				"insert into schema_version (id, version) values (1, $1) on conflict (id) do update set version = excluded.version",
				connection, transaction);
			upsert.Parameters.AddWithValue(lastApplied);
			await upsert.ExecuteNonQueryAsync(cancellationToken);
		}

		await transaction.CommitAsync(cancellationToken);
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

	private static async Task<int> ReadVersionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
		CancellationToken cancellationToken)
	{
		await using var command = new NpgsqlCommand("select version from schema_version where id = 1", connection,
			transaction);
		var value = await command.ExecuteScalarAsync(cancellationToken);
		return value is null ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
	}
}