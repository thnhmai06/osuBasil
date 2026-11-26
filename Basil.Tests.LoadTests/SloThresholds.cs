using Basil.Tests.Shared.Environment.Monitor;
using Basil.Tests.Shared.Workload.Outcomes;
using NBomber.Contracts;

namespace Basil.Tests.LoadTests;

/// <summary>Declares the W15 SLO thresholds shared across load test modules.</summary>
public static class SloThresholds
{
	/// <summary>The highest server CPU, in percent, the SLO allows; the run verdict checks the same limit after the run.</summary>
	public const double MaxServerCpuPercent = 80.0;

	/// <summary>Returns the standard per-channel and error-rate SLO thresholds for a scenario.</summary>
	public static Threshold[] ForScenario(bool includePoll = false, bool includeSubmit = false,
		bool includeGetScores = false, bool includeRest = false, bool includeLogin = false,
		bool includeServerCpu = false, double maxErrorPercent = 0.5)
	{
		var thresholds = new List<Threshold>
		{
			Threshold.Create(stats => stats.Fail.Request.Percent < maxErrorPercent)
		};

		if (includePoll)
		{
			thresholds.Add(Threshold.Create(Channels.Poll, stats => stats.Ok.Latency.Percent95 <= 200));
			thresholds.Add(Threshold.Create(Channels.Poll, stats => stats.Ok.Latency.Percent99 <= 500));
		}

		if (includeSubmit)
			thresholds.Add(Threshold.Create(Channels.Submit, stats => stats.Ok.Latency.Percent95 <= 1000));

		if (includeGetScores)
			thresholds.Add(Threshold.Create(Channels.GetScores, stats => stats.Ok.Latency.Percent95 <= 500));

		if (includeRest)
			thresholds.Add(Threshold.Create(Channels.Rest, stats => stats.Ok.Latency.Percent95 <= 300));

		if (includeLogin)
			thresholds.Add(Threshold.Create(Channels.Login, stats => stats.Ok.Latency.Percent95 <= 2000));

		if (includeServerCpu)
			thresholds.Add(Threshold.Create(
				metric => metric.Gauges.Get(DiagnosticsMonitor.CpuGaugeName).Value <= MaxServerCpuPercent,
				startCheckAfter: TimeSpan.FromSeconds(15)));

		return [.. thresholds];
	}
}