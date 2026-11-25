using Basil.Tests.Shared.Environment.Monitor;
using Basil.Tests.Shared.Workload.Personas;
using NBomber.Contracts;
using NBomber.CSharp;

namespace Basil.Tests.LoadTests.Stress;

/// <summary>Stress test: steps from 1x to 2x ceiling, then drops back to 0.5x to measure recovery time.</summary>
public sealed class StressTest : ILoadTest
{
	private static readonly double[] Multipliers = [1.0, 1.25, 1.5, 1.75, 2.0, 0.5];

	public string Name => "stress";

	public string ReportName(LoadTestOptions options)
	{
		return $"stress-n{options.TargetSessions}-s{options.Seed}";
	}

	public IReadOnlyList<ScenarioProps> BuildScenarios(LoadTestOptions options, WorkloadContext workload,
		DiagnosticsMonitor? monitor)
	{
		var stepDuration = TimeSpan.FromMinutes(Math.Max(1, options.StepMinutes));
		var rampStep = TimeSpan.FromSeconds(Math.Max(20, options.RampDuration(options.TargetSessions / 4).TotalSeconds));
		var allocations = Multipliers
			.Select(m => PersonaAllocation.ForSessions((int)Math.Round(options.TargetSessions * m)))
			.ToArray();

		return ScenarioCatalog.BuildFullSuite(
			workload,
			monitor,
			pick =>
			{
				var sims = new List<LoadSimulation>();
				foreach (var alloc in allocations)
				{
					var copies = pick(alloc);
					sims.Add(Simulation.RampingConstant(copies, rampStep));
					sims.Add(Simulation.KeepConstant(copies, stepDuration));
				}

				sims.Add(Simulation.RampingConstant(0, rampStep));
				return [.. sims];
			},
			pick =>
			{
				var sims = new List<LoadSimulation>();
				foreach (var alloc in allocations)
				{
					sims.Add(Simulation.Inject(pick(alloc), TimeSpan.FromMinutes(1), rampStep + stepDuration));
				}

				return [.. sims];
			},
			enforceSlo: false,
			warmUp: TimeSpan.FromSeconds(20));
	}
}
