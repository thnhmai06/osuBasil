namespace Basil.LoadTests.Analysis;

/// <summary>
///     Records the first occurrence of each failure class during a stress run, with the load-schedule
///     phase and concurrency level in effect at the time — so a report reader can tell "the server
///     started refusing connections at 1000 concurrent users" from "timeouts started at 250", and can
///     tell either from "these were during the final ramp-down to 0, not a genuine load level" (a
///     failure logged during ramp-down says so explicitly in its level label rather than being
///     misattributed to the last real level — see 2026-08-14's verify-stress post-mortem, where errors
///     during ramp-down were reported as level 100). Each class is recorded once; the run is expected to
///     keep going past it (see <c>StressSettings.MaxFailCount</c>).
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
	private bool _hasConnectionFailure;
	private bool _hasServerError;
	private bool _hasTimeout;

	/// <summary>Records the first request timeout, if not already recorded.</summary>
	public void RecordTimeout(string levelLabel)
	{
		RecordFirst(ref _hasTimeout, "first-timeout", levelLabel, null);
	}

	/// <summary>Records the first connection-level failure (refused, reset, port exhaustion), if not already recorded.</summary>
	public void RecordConnectionFailure(string levelLabel, string detail)
	{
		RecordFirst(ref _hasConnectionFailure, "first-connection-failure", levelLabel, detail);
	}

	/// <summary>Records the first server-reported error (a login failure reason, an HTTP 5xx, ...), if not already recorded.</summary>
	public void RecordServerError(string levelLabel, string detail)
	{
		RecordFirst(ref _hasServerError, "first-server-error", levelLabel, detail);
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

	private void RecordFirst(ref bool flag, string label, string levelLabel, string? detail)
	{
		lock (_lock)
		{
			if (flag) return;
			flag = true;
			_rows.Add($"| {label} | {DateTimeOffset.UtcNow:O} | {levelLabel} | {detail ?? ""} |");
		}
	}
}