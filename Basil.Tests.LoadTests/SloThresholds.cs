using Basil.Tests.Shared.Workload.Outcomes;
using NBomber.Contracts;

namespace Basil.Tests.LoadTests;

/// <summary>Declares the W15 SLO thresholds shared across load test modules.</summary>
public static class SloThresholds
{
	/// <summary>
	///     The server CPU, in percent, that the 95th percentile of the samples of a step may not exceed while the step
	///     holds its load; the run verdict judges it after the run, and the ramps are reported but not judged.
	/// </summary>
	public const double MaxServerCpuPercent = 80.0;

	/// <summary>How long after the planned end of a ramp the load counts as held, to absorb the start-up offset.</summary>
	public static readonly TimeSpan SteadyMargin = TimeSpan.FromSeconds(5);

	/// <summary>Returns the standard per-channel and error-rate SLO thresholds for a scenario.</summary>
	public static Threshold[] ForScenario(bool includePoll = false, bool includeSubmit = false,
		bool includeGetScores = false, bool includeRest = false, bool includeLogin = false,
		double maxErrorPercent = 0.5)
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

		return [.. thresholds];
	}
}