using Basil.LoadTests.Helpers;
using Basil.LoadTests.Infrastructure.Metrics;

namespace Basil.LoadTests.Analysis;

/// <summary>The leak-slope verdict for one resource series over a soak run's steady-state window.</summary>
public sealed record SeriesVerdict(string Series, double? SlopePerHour, double? RSquared, string Verdict);

/// <summary>
///     Fits a line to each resource series over a soak run's steady-state window (after warm-up, before
///     the configured duration ends, so neither ramp-up nor teardown noise can fake a slope) and
///     classifies it stable/watch/leak against the profile's configured per-hour thresholds. A window
///     shorter than <see cref="MinEvaluationWindow" /> is reported as insufficient data rather than
///     extrapolated into a verdict.
/// </summary>
public static class SoakAnalyzer
{
	/// <summary>
	///     Minimum steady-state window (after warm-up, before the scenario's configured duration ends)
	///     a leak-slope fit needs before its verdict is trusted. Below this, a short run's own warm-up
	///     transient can dominate the fit and get extrapolated ×many into a false "leak" — see the
	///     2026-08-14 soak report post-mortem (93s run, warm-up climb alone produced a reported
	///     +817 MB/hr working-set slope with no leak actually present).
	/// </summary>
	private static readonly TimeSpan MinEvaluationWindow = TimeSpan.FromMinutes(30);

	/// <summary>Analyzes <paramref name="timeline" />'s memory/thread/handle series for a sustained upward trend.</summary>
	/// <param name="timeline">Full-run resource samples.</param>
	/// <param name="warmUp">Leading window excluded as ramp-up noise.</param>
	/// <param name="duration">
	///     The soak scenario's configured hold duration. Samples taken after <paramref name="warmUp" /> +
	///     <paramref name="duration" /> (teardown/drain, or trailing idle sampling before the process
	///     exits) are excluded from the fit for the same reason warm-up is: they are not steady-state load.
	/// </param>
	/// <param name="thresholds">Per-series leak-slope thresholds, keyed by <c>{Series}PerHour</c>.</param>
	public static IReadOnlyList<SeriesVerdict> Analyze(
		ResourceTimeline timeline, TimeSpan warmUp, TimeSpan duration, IReadOnlyDictionary<string, double> thresholds)
	{
		if (timeline.Samples.Count == 0) return [];

		var start = timeline.Samples[0].TimestampUtc;
		var windowEnd = warmUp + duration;
		var steadyState = timeline.Samples
			.Where(s => s.TimestampUtc - start >= warmUp && s.TimestampUtc - start <= windowEnd)
			.ToList();

		var evaluationWindow = steadyState.Count > 0
			? steadyState[^1].TimestampUtc - steadyState[0].TimestampUtc
			: TimeSpan.Zero;
		if (evaluationWindow < MinEvaluationWindow)
			return
			[
				new SeriesVerdict("Working set (memory leak)", null, null, InsufficientDataVerdict(evaluationWindow)),
				new SeriesVerdict("GC heap (memory leak)", null, null, InsufficientDataVerdict(evaluationWindow)),
				new SeriesVerdict("Thread count (thread leak)", null, null, InsufficientDataVerdict(evaluationWindow)),
				new SeriesVerdict("Threadpool threads (thread leak)", null, null,
					InsufficientDataVerdict(evaluationWindow)),
				new SeriesVerdict("Handle count (socket/handle leak)", null, null,
					InsufficientDataVerdict(evaluationWindow)),
				new SeriesVerdict("TCP connections (socket leak)", null, null,
					InsufficientDataVerdict(evaluationWindow))
			];

		var results = new List<SeriesVerdict>
		{
			Evaluate(steadyState, start, "Working set (memory leak)", "WorkingSetMbPerHour",
				s => s.WorkingSetBytes / (1024.0 * 1024), thresholds),
			Evaluate(steadyState, start, "GC heap (memory leak)", "GcHeapMbPerHour",
				s => s.GcHeapBytes / (1024.0 * 1024), thresholds),
			Evaluate(steadyState, start, "Thread count (thread leak)", "ThreadsPerHour",
				s => s.ThreadCount, thresholds),
			Evaluate(steadyState, start, "Threadpool threads (thread leak)", "ThreadsPerHour",
				s => s.ThreadPoolThreads, thresholds),
			Evaluate(steadyState, start, "Handle count (socket/handle leak)", "HandlesPerHour",
				s => s.HandleCount, thresholds),
			Evaluate(steadyState, start, "TCP connections (socket leak)", "HandlesPerHour",
				s => s.TcpConnections, thresholds)
		};

		return results;
	}

	private static string InsufficientDataVerdict(TimeSpan evaluationWindow)
	{
		return $"insufficient data ({evaluationWindow.TotalMinutes:F0}min steady-state window < " +
		       $"{MinEvaluationWindow.TotalMinutes:F0}min minimum)";
	}

	/// <summary>Writes <c>soak-analysis.md</c>.</summary>
	public static async Task WriteReportAsync(string reportFolder, IReadOnlyList<SeriesVerdict> verdicts)
	{
		var lines = new List<string>
		{
			"# Soak analysis",
			"",
			"| Series | Slope (per hour) | R² | Verdict |",
			"|---|---|---|---|"
		};
		lines.AddRange(verdicts.Select(v =>
			$"| {v.Series} | {(v.SlopePerHour.HasValue ? v.SlopePerHour.Value.ToString("F3") : "n/a")} | " +
			$"{(v.RSquared.HasValue ? v.RSquared.Value.ToString("F2") : "n/a")} | {v.Verdict} |"));

		lines.Add("");
		lines.Add("Latency creep (p95 per reporting interval) is not evaluated here — NBomber's own report is the " +
		          "source of truth for latency percentiles, and this project deliberately does not re-derive them; " +
		          "compare p95 across the reporting-interval snapshots in NBomber's own report/CSV by hand.");

		await File.WriteAllLinesAsync(Path.Combine(reportFolder, "soak-analysis.md"), lines);
	}

	private static SeriesVerdict Evaluate(IReadOnlyList<ResourceSample> samples, DateTimeOffset start, string label,
		string thresholdKey, Func<ResourceSample, double?> selector, IReadOnlyDictionary<string, double> thresholds)
	{
		var points = samples
			.Select(s => (X: (s.TimestampUtc - start).TotalSeconds, Y: selector(s)))
			.Where(p => p.Y.HasValue)
			.Select(p => (p.X, Y: p.Y!.Value))
			.ToList();

		var fit = LinearFit.Fit(points);
		if (fit is null) return new SeriesVerdict(label, null, null, "not available on this host");

		var threshold = thresholds.GetValueOrDefault(thresholdKey, double.MaxValue);
		var verdict = fit.SlopePerHour > threshold && fit.RSquared >= 0.5
			? "leak"
			: fit is { SlopePerHour: > 0, RSquared: >= 0.3 }
				? "watch"
				: "stable";

		return new SeriesVerdict(label, fit.SlopePerHour, fit.RSquared, verdict);
	}
}