using System.Diagnostics;
using Basil.Application.Diagnostics;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Persistence;

/// <summary>
///     Times a SQLite write operation and records <c>SQLITE_BUSY</c> occurrences, tagged by an
///     operation name. Applied only to the write paths named in the 2026 performance investigation's
///     ADR-001 (login, client-hash upsert, round create/end, score create, stat increment, first-login
///     privilege bootstrap) — not uniformly across every repository method, since the read paths are
///     not the bottleneck under investigation and adding instrumentation everywhere would obscure the
///     signal this exists to capture.
/// </summary>
internal static class SqliteInstrumentation
{
	/// <summary>Times <paramref name="action" />, recording duration and BUSY occurrences under <paramref name="operation" />.</summary>
	public static async Task<T> RecordAsync<T>(string operation, Func<Task<T>> action)
	{
		var startedAt = Stopwatch.GetTimestamp();
		try
		{
			return await action();
		}
		catch (SqliteException ex) when (ex.SqliteErrorCode == 5)
		{
			BasilMetrics.DbBusyCount.Add(1, new KeyValuePair<string, object?>("operation", operation));
			throw;
		}
		finally
		{
			BasilMetrics.DbCommandDurationMs.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
				new KeyValuePair<string, object?>("operation", operation));
		}
	}

	/// <summary>Times <paramref name="action" />, recording duration and BUSY occurrences under <paramref name="operation" />.</summary>
	public static async Task RecordAsync(string operation, Func<Task> action)
	{
		var startedAt = Stopwatch.GetTimestamp();
		try
		{
			await action();
		}
		catch (SqliteException ex) when (ex.SqliteErrorCode == 5)
		{
			BasilMetrics.DbBusyCount.Add(1, new KeyValuePair<string, object?>("operation", operation));
			throw;
		}
		finally
		{
			BasilMetrics.DbCommandDurationMs.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
				new KeyValuePair<string, object?>("operation", operation));
		}
	}
}