using System.Text.Json;
using System.Text.Json.Serialization;
using Basil.LoadTests.Helpers;
using Basil.LoadTests.Infrastructure.Metrics;

namespace Basil.LoadTests.Infrastructure.Reporting;

/// <summary>
///     Writes the artifacts this project owns for a run — <c>run.json</c>, <c>resources.csv</c>, and
///     <c>summary.md</c> — alongside whatever NBomber's own <c>Html</c>/<c>Csv</c>/<c>Md</c>/<c>Txt</c>
///     reports already wrote into the same folder. Nothing NBomber already reports (latency
///     percentiles, RPS, failure counts) is recomputed here.
/// </summary>
public static class ReportWriter
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter() }
	};

	/// <summary>Writes <c>run.json</c> for the given manifest.</summary>
	public static async Task WriteRunJsonAsync(string reportFolder, RunManifest manifest,
		CancellationToken cancellationToken = default)
	{
		Directory.CreateDirectory(reportFolder);
		var json = JsonSerializer.Serialize(manifest, JsonOptions);
		await File.WriteAllTextAsync(Path.Combine(reportFolder, "run.json"), json, cancellationToken);
	}

	/// <summary>Writes <c>resources.csv</c> from the accumulated resource timeline.</summary>
	public static void WriteResourcesCsv(string reportFolder, ResourceTimeline timeline)
	{
		timeline.WriteCsv(Path.Combine(reportFolder, "resources.csv"));
	}

	/// <summary>Writes the human-readable <c>summary.md</c>: environment, resource aggregates, and notes.</summary>
	public static async Task WriteSummaryMarkdownAsync(string reportFolder, RunManifest manifest,
		ResourceTimeline timeline, CancellationToken cancellationToken = default)
	{
		var aggregates = timeline.Aggregate();
		var duration = manifest.FinishedUtc.HasValue
			? manifest.FinishedUtc.Value - manifest.StartedUtc
			: (TimeSpan?)null;

		var lines = new List<string>
		{
			$"# Load test run: {manifest.ProfileName}",
			"",
			"## Environment",
			$"- Started: {manifest.StartedUtc:u}",
			$"- Duration: {(duration.HasValue ? TimeSpanFormat.Humanize(duration.Value) : "in progress")}",
			$"- OS: {manifest.OsDescription} ({manifest.OsArchitecture})",
			$"- Runtime: {manifest.FrameworkDescription}",
			$"- Logical processors: {manifest.ProcessorCount}",
			$"- Git commit: {manifest.GitCommit ?? "unknown"}",
			$"- Server host kind: {manifest.Profile.ServerHost.Kind}",
			"",
			manifest.StartupTime.HasValue
				? $"- Server startup time: {TimeSpanFormat.Humanize(manifest.StartupTime.Value)}"
				: "- Server startup time: not available on this host",
			"",
			"## Host capabilities",
			$"- Process metrics (CPU/working set/threads/handles): {(manifest.Capabilities.CanMeasureProcessMetrics ? "available" : "not available on this host")}",
			$"- GC/allocation/threadpool counters: {(manifest.Capabilities.CanMeasureDotnetCounters ? "available" : "not available on this host")}",
			$"- Database snapshot/restore: {(manifest.Capabilities.CanSnapshotDatabase ? "available" : "not available on this host")}",
			"",
			"## Resource usage (min / mean / max)",
			"| Metric | Min | Mean | Max |",
			"|---|---|---|---|"
		};

		AddRow(lines, aggregates, "CpuPercent", "CPU %", 2);
		AddRow(lines, aggregates, "WorkingSetBytes", "Working set (MB)", 0, 1.0 / (1024 * 1024));
		AddRow(lines, aggregates, "PrivateMemoryBytes", "Private memory (MB)", 0, 1.0 / (1024 * 1024));
		AddRow(lines, aggregates, "GcHeapBytes", "GC heap (MB)", 0, 1.0 / (1024 * 1024));
		AddRow(lines, aggregates, "Gen0Collections", "Gen0 collections", 0);
		AddRow(lines, aggregates, "Gen1Collections", "Gen1 collections", 0);
		AddRow(lines, aggregates, "Gen2Collections", "Gen2 collections", 0);
		AddRow(lines, aggregates, "AllocRateBytesPerSecond", "Alloc rate (MB/s)", 2, 1.0 / (1024 * 1024));
		AddRow(lines, aggregates, "ThreadCount", "Thread count", 0);
		AddRow(lines, aggregates, "HandleCount", "Handle count", 0);
		AddRow(lines, aggregates, "ThreadPoolThreads", "Threadpool threads", 0);
		AddRow(lines, aggregates, "TcpConnections", "TCP connections", 0);
		AddRow(lines, aggregates, "ThreadPoolQueueLength", "Threadpool queue length", 0);
		AddRow(lines, aggregates, "GcPausePercent", "% time in GC", 2);
		AddRow(lines, aggregates, "TotalMachineCpuPercent", "Total machine CPU %", 2);
		lines.Add("");

		if (manifest.Notes.Count > 0)
		{
			lines.Add("## Notes");
			lines.AddRange(manifest.Notes.Select(note => $"- {note}"));
			lines.Add("");
		}

		await File.WriteAllLinesAsync(Path.Combine(reportFolder, "summary.md"), lines, cancellationToken);
	}

	private static void AddRow(List<string> lines, IReadOnlyDictionary<string, FieldAggregate> aggregates,
		string key, string label, int decimals, double scale = 1.0)
	{
		var a = aggregates.TryGetValue(key, out var value) ? value : new FieldAggregate(null, null, null);
		lines.Add(
			$"| {label} | {Format(a.Min, decimals, scale)} | {Format(a.Mean, decimals, scale)} | {Format(a.Max, decimals, scale)} |");
	}

	private static string Format(double? value, int decimals, double scale)
	{
		return value.HasValue ? (value.Value * scale).ToString($"F{decimals}") : "n/a";
	}
}