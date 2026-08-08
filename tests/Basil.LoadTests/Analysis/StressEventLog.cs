namespace Basil.LoadTests.Analysis;

/// <summary>
///     Records the first occurrence of each failure class during a stress run, with the concurrency
///     level in effect at the time — so a report reader can tell "the server started refusing
///     connections at 1000 concurrent users" from "timeouts started at 250". Each class is recorded
///     once; the run is expected to keep going past it (see <c>StressSettings.MaxFailCount</c>).
/// </summary>
/// <remarks>
///     CPU saturation and database-bottleneck signatures are not auto-detected here — they are read
///     from <c>resources.csv</c> by cross-referencing its timestamps against this log, since building
///     a reliable automatic detector for either is a separate piece of analysis work. <c>summary.md</c>
///     notes this explicitly rather than reporting false "no saturation observed".
/// </remarks>
public sealed class StressEventLog
{
	private readonly Lock _lock = new();
	private readonly List<string> _rows = [];
	private bool _hasTimeout;
	private bool _hasConnectionFailure;
	private bool _hasServerError;

	/// <summary>Records the first request timeout, if not already recorded.</summary>
	public void RecordTimeout(int concurrencyLevel)
	{
		RecordFirst(ref _hasTimeout, "first-timeout", concurrencyLevel, null);
	}

	/// <summary>Records the first connection-level failure (refused, reset, port exhaustion), if not already recorded.</summary>
	public void RecordConnectionFailure(int concurrencyLevel, string detail)
	{
		RecordFirst(ref _hasConnectionFailure, "first-connection-failure", concurrencyLevel, detail);
	}

	/// <summary>Records the first server-reported error (a login failure reason, an HTTP 5xx, ...), if not already recorded.</summary>
	public void RecordServerError(int concurrencyLevel, string detail)
	{
		RecordFirst(ref _hasServerError, "first-server-error", concurrencyLevel, detail);
	}

	/// <summary>Writes <c>stress-events.md</c>.</summary>
	public void WriteReport(string reportFolder)
	{
		var lines = new List<string>
		{
			"# Stress event log",
			"",
			"| Event | Timestamp (UTC) | Concurrency level | Detail |",
			"|---|---|---|---|"
		};

		lock (_lock)
		{
			lines.AddRange(_rows);
		}

		if (lines.Count == 4) lines.Add("| (no failures observed at any configured concurrency level) | | | |");

		lines.Add("");
		lines.Add("CPU saturation and database-bottleneck signatures are not auto-detected above — cross-reference " +
		          "`resources.csv` (CPU%, and rising login/write failures while CPU is *not* saturated is the " +
		          "SQLite-writer-serialization signature) against the timestamps here.");

		File.WriteAllLines(Path.Combine(reportFolder, "stress-events.md"), lines);
	}

	private void RecordFirst(ref bool flag, string label, int concurrencyLevel, string? detail)
	{
		lock (_lock)
		{
			if (flag) return;
			flag = true;
			_rows.Add($"| {label} | {DateTimeOffset.UtcNow:O} | {concurrencyLevel} | {detail ?? ""} |");
		}
	}
}
