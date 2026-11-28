using Basil.Tests.Shared.Environment.Monitor;
using Basil.Tests.Shared.Workload.Personas;
using NBomber.Contracts;
using NBomber.CSharp;

namespace Basil.Tests.LoadTests.Soak;

/// <summary>Soak test: holds 70 % of N for 6 hours (configurable), split into per-hour windows with full SLO thresholds.</summary>
public sealed class SoakTest : ILoadTest
{
	private static readonly TimeSpan WarmUp = TimeSpan.FromSeconds(30);

	public string Name => "soak";

	public string ReportName(LoadTestOptions options)
	{
		var soakN = SoakSessions(options);
		return $"soak-n{soakN}-{(int)Math.Ceiling(options.SoakDuration.TotalHours)}h-s{options.Seed}";
	}

	public RunPlan Plan(LoadTestOptions options)
	{
		var soakN = SoakSessions(options);
		var ramp = options.RampDuration(soakN);
		var rampDown = TimeSpan.FromSeconds(Math.Max(15, ramp.TotalSeconds / 2));
		var windows = SplitWindows(options.SoakDuration, TimeSpan.FromHours(1));
		List<(string, int, TimeSpan, TimeSpan)> levels =
		[
			("ramp", soakN, ramp, ramp),
			.. windows.Select((window, i) => ($"hour {i + 1}", soakN, TimeSpan.Zero, window))
		];
		return RunPlan.Create(WarmUp, rampDown, [.. levels]);
	}

	public IReadOnlyList<ScenarioProps> BuildScenarios(LoadTestOptions options, WorkloadContext workload,
		DiagnosticsMonitor? monitor)
	{
		var soakN = SoakSessions(options);
		var alloc = PersonaAllocation.ForSessions(soakN);
		var ramp = options.RampDuration(soakN);
		var rampDown = TimeSpan.FromSeconds(Math.Max(15, ramp.TotalSeconds / 2));
		var windows = SplitWindows(options.SoakDuration, TimeSpan.FromHours(1));

		return ScenarioCatalog.BuildFullSuite(
			workload,
			monitor,
			pick =>
			{
				var copies = pick(alloc);
				List<LoadSimulation> sims =
				[
					Simulation.RampingConstant(copies, ramp),
					.. windows.Select(window => Simulation.KeepConstant(copies, window)),
					Simulation.RampingConstant(0, rampDown)
				];
				return [.. sims];
			},
			pick => [Injection.PerMinute(pick(alloc), ramp + options.SoakDuration + rampDown)],
			true,
			WarmUp);
	}

	private static int SoakSessions(LoadTestOptions options)
	{
		return Math.Max(10, (int)Math.Round(options.TargetSessions * 0.70));
	}

	private static List<TimeSpan> SplitWindows(TimeSpan total, TimeSpan windowSize)
	{
		var result = new List<TimeSpan>();
		var remaining = total;
		while (remaining > TimeSpan.Zero)
		{
			var slice = remaining > windowSize ? windowSize : remaining;
			result.Add(slice);
			remaining -= slice;
		}

		return result;
	}
}