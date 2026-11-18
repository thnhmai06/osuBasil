using System.Data.Common;
using System.Globalization;

namespace Basil.Testing.Persistence;

public static class StoredRows
{
	public static async Task<IReadOnlyList<StoredRow>> ReadAsync(string connectionString, string sql,
		IReadOnlyDictionary<string, object?>? parameters, CancellationToken ct)
	{
		await using var connection = await TestServer.OpenAsync(connectionString, ct);
		await using var command = Command(connection, sql, parameters);
		await using var reader = await command.ExecuteReaderAsync(ct);
		var rows = new List<StoredRow>();
		while (await reader.ReadAsync(ct))
		{
			var values = new Dictionary<string, object?>(reader.FieldCount, StringComparer.OrdinalIgnoreCase);
			for (var column = 0; column < reader.FieldCount; column++)
				values[reader.GetName(column)] = await reader.IsDBNullAsync(column, ct)
					? null
					: reader.GetValue(column);
			rows.Add(new StoredRow(values));
		}

		return rows;
	}

	public static async Task<int> ExecuteAsync(string connectionString, string sql,
		IReadOnlyDictionary<string, object?>? parameters, CancellationToken ct)
	{
		await using var connection = await TestServer.OpenAsync(connectionString, ct);
		await using var command = Command(connection, sql, parameters);
		return await command.ExecuteNonQueryAsync(ct);
	}

	public static async Task<int> InsertAsync(string connectionString, string table,
		IEnumerable<IReadOnlyDictionary<string, object?>> rows, CancellationToken ct)
	{
		await using var connection = await TestServer.OpenAsync(connectionString, ct);
		await using var transaction = await connection.BeginTransactionAsync(ct);
		var stored = 0;
		foreach (var row in rows)
		{
			var columns = row.Keys.ToArray();
			var sql = $"insert into {table} ({string.Join(", ", columns)}) values " +
			          $"({string.Join(", ", columns.Select(column => "@" + column))})";
			await using var command = Command(connection, sql,
				row.ToDictionary(pair => "@" + pair.Key, pair => pair.Value));
			command.Transaction = transaction;
			stored += await command.ExecuteNonQueryAsync(ct);
		}

		await transaction.CommitAsync(ct);
		return stored;
	}

	private static DbCommand Command(DbConnection connection, string sql,
		IReadOnlyDictionary<string, object?>? parameters)
	{
		var command = connection.CreateCommand();
		command.CommandText = sql;
		if (parameters is null) return command;

		foreach (var (name, value) in parameters)
		{
			var parameter = command.CreateParameter();
			parameter.ParameterName = name;
			parameter.Value = value ?? DBNull.Value;
			command.Parameters.Add(parameter);
		}

		return command;
	}

	public static async Task<IReadOnlyList<StoredRow>> WaitForAsync(string connectionString, string sql,
		IReadOnlyDictionary<string, object?>? parameters, Func<IReadOnlyList<StoredRow>, bool> satisfied,
		string what, CancellationToken ct)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
		while (true)
		{
			var rows = await ReadAsync(connectionString, sql, parameters, ct);
			if (satisfied(rows)) return rows;
			if (DateTime.UtcNow >= deadline)
				throw new TimeoutException(
					$"The database did not hold {what} before the deadline; the query returned {rows.Count} rows.");
			await Task.Delay(50, ct);
		}
	}
}

public sealed record StoredRow(IReadOnlyDictionary<string, object?> Values)
{
	public object? this[string column] => Values[column];

	public string? Text(string column)
	{
		return Values[column] as string;
	}

	public long Number(string column)
	{
		return Convert.ToInt64(Values[column], CultureInfo.InvariantCulture);
	}

	public bool Bool(string column)
	{
		return (bool)Values[column]!;
	}

	public DateTimeOffset Instant(string column)
	{
		return Values[column] switch
		{
			DateTimeOffset offset => offset,
			DateTime time => new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Utc)),
			_ => throw new InvalidOperationException($"The column {column} does not hold a timestamp.")
		};
	}
}