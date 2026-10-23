using System.Globalization;
using Npgsql;

namespace Basil.Infrastructure.Persistence.Tests.Support;

/// <summary>Reads what the server's queued writes have committed, straight from the database.</summary>
internal static class StoredRows
{
	/// <summary>Reads the rows a query returns, as the values of their columns.</summary>
	/// <param name="connectionString">How to reach the database.</param>
	/// <param name="sql">The query that reads the rows.</param>
	/// <param name="parameters">The values the query needs, by parameter name.</param>
	/// <param name="cancellationToken">A token that cancels the read.</param>
	/// <returns>The rows the query returned.</returns>
	public static async Task<IReadOnlyList<StoredRow>> ReadAsync(string connectionString, string sql,
		IReadOnlyDictionary<string, object?>? parameters, CancellationToken cancellationToken)
	{
		await using var connection = new NpgsqlConnection(connectionString);
		await connection.OpenAsync(cancellationToken);
		await using var command = new NpgsqlCommand(sql, connection);
		if (parameters is not null)
			foreach (var (name, value) in parameters)
				command.Parameters.AddWithValue(name, value ?? DBNull.Value);

		await using var reader = await command.ExecuteReaderAsync(cancellationToken);
		var rows = new List<StoredRow>();
		while (await reader.ReadAsync(cancellationToken))
		{
			var values = new Dictionary<string, object?>(reader.FieldCount, StringComparer.OrdinalIgnoreCase);
			for (var column = 0; column < reader.FieldCount; column++)
				values[reader.GetName(column)] = await reader.IsDBNullAsync(column, cancellationToken)
					? null
					: reader.GetValue(column);
			rows.Add(new StoredRow(values));
		}

		return rows;
	}

	/// <summary>Waits until the rows a query returns satisfy the condition.</summary>
	/// <param name="connectionString">How to reach the database.</param>
	/// <param name="sql">The query that reads the rows.</param>
	/// <param name="parameters">The values the query needs, by parameter name.</param>
	/// <param name="satisfied">What the rows must look like.</param>
	/// <param name="what">What is waited for, named in the failure message.</param>
	/// <param name="cancellationToken">A token that cancels the wait.</param>
	/// <returns>The rows the query returned.</returns>
	public static async Task<IReadOnlyList<StoredRow>> WaitForAsync(string connectionString, string sql,
		IReadOnlyDictionary<string, object?>? parameters, Func<IReadOnlyList<StoredRow>, bool> satisfied,
		string what, CancellationToken cancellationToken)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
		while (true)
		{
			var rows = await ReadAsync(connectionString, sql, parameters, cancellationToken);
			if (satisfied(rows)) return rows;
			if (DateTime.UtcNow >= deadline)
				throw new TimeoutException(
					$"The database did not hold {what} before the deadline; the query returned {rows.Count} rows.");
			await Task.Delay(50, cancellationToken);
		}
	}
}

/// <summary>One stored row, as the values of its columns.</summary>
/// <param name="Values">The value of each column, by its name.</param>
internal sealed record StoredRow(IReadOnlyDictionary<string, object?> Values)
{
	/// <summary>Gets the value of a column.</summary>
	/// <param name="column">The name of the column.</param>
	public object? this[string column] => Values[column];

	/// <summary>Gets the value of a column as text.</summary>
	/// <param name="column">The name of the column.</param>
	/// <returns>The text, or <see langword="null" /> when the column holds nothing.</returns>
	public string? Text(string column) => Values[column] as string;

	/// <summary>Gets the value of a column as a number.</summary>
	/// <param name="column">The name of the column.</param>
	/// <returns>The number.</returns>
	public long Number(string column) => Convert.ToInt64(Values[column], CultureInfo.InvariantCulture);

	/// <summary>Gets the value of a column as a yes or no.</summary>
	/// <param name="column">The name of the column.</param>
	/// <returns>The value of the column.</returns>
	public bool Bool(string column) => (bool)Values[column]!;

	/// <summary>Gets the value of a column as the instant it names.</summary>
	/// <param name="column">The name of the column.</param>
	/// <returns>The instant, in UTC.</returns>
	public DateTimeOffset Instant(string column) => Values[column] switch
	{
		DateTimeOffset offset => offset,
		DateTime time => new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Utc)),
		_ => throw new InvalidOperationException($"The column {column} does not hold a timestamp.")
	};
}
