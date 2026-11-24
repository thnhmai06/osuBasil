using System.Diagnostics;

namespace LoadHarness;

/// <summary>Collects the outcome and latency of every request, per kind of client, while the run is being measured.</summary>
internal sealed class Recorder
{
	private readonly Dictionary<string, Kind> _kinds = [];
	private readonly Lock _lock = new();
	private long _measuringSince;

	public bool Measuring { get; private set; }

	public TimeSpan Elapsed => _measuringSince == 0 ? TimeSpan.Zero : Stopwatch.GetElapsedTime(_measuringSince);

	public void StartMeasuring()
	{
		_measuringSince = Stopwatch.GetTimestamp();
		Measuring = true;
	}

	public void StopMeasuring()
	{
		Measuring = false;
	}

	/// <summary>Records one request; a request outside the measured window is dropped unless <paramref name="always"/> is set.</summary>
	/// <param name="failure">Why it failed, or null when it succeeded.</param>
	/// <param name="always">Counts a failure of the setup (sign-in, connect) even during the ramp.</param>
	public void Record(string kind, TimeSpan latency, string? failure = null, bool always = false)
	{
		if (!Measuring && !(always && failure is not null)) return;
		lock (_lock)
		{
			if (!_kinds.TryGetValue(kind, out var entry)) _kinds[kind] = entry = new Kind();
			entry.Requests++;
			if (failure is null) entry.Latencies.Add(latency.TotalMilliseconds);
			else entry.Failures[failure] = entry.Failures.GetValueOrDefault(failure) + 1;
		}
	}

	public Dictionary<string, KindReport> Report(TimeSpan measured)
	{
		lock (_lock)
		{
			return _kinds.OrderBy(pair => pair.Key).ToDictionary(pair => pair.Key, pair =>
			{
				var sorted = pair.Value.Latencies.Order().ToArray();
				var failed = pair.Value.Failures.Values.Sum();
				return new KindReport(pair.Value.Requests, failed,
					pair.Value.Failures.OrderByDescending(f => f.Value).ToDictionary(f => f.Key, f => f.Value),
					Percentile(sorted, 0.50), Percentile(sorted, 0.95), Percentile(sorted, 0.99),
					measured.TotalSeconds > 0 ? Math.Round(pair.Value.Requests / measured.TotalSeconds, 1) : 0);
			});
		}
	}

	private static double Percentile(double[] sorted, double p)
	{
		if (sorted.Length == 0) return 0;
		var index = Math.Min(sorted.Length - 1, Math.Max(0, (int)Math.Ceiling(p * sorted.Length) - 1));
		return Math.Round(sorted[index], 2);
	}

	private sealed class Kind
	{
		public readonly Dictionary<string, int> Failures = [];
		public readonly List<double> Latencies = [];
		public long Requests;
	}
}

internal sealed record KindReport(
	long Requests,
	long Failed,
	Dictionary<string, int> FailuresByReason,
	double P50Ms,
	double P95Ms,
	double P99Ms,
	double RequestsPerSecond);
