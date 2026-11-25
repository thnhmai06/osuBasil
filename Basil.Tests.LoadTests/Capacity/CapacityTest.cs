using Basil.Tests.Shared.Environment.Monitor;
using Basil.Tests.Shared.Workload.Personas;
using NBomber.Contracts;
using NBomber.CSharp;

namespace Basil.Tests.LoadTests.Capacity;

/// <summary>Capacity test: adds <c>+ΔN</c> every 5 minutes until the SLO breaks; the pilot is a shorter Capacity run.</summary>
public sealed class CapacityTest : ILoadTest
{
	public string Name => "capacity";

	public string ReportName(LoadTestOptions options)
	{
		var prefix = options.Pilot ? "pilot" : "capacity";
		return $"{prefix}-d{options.StepDelta}-x{options.StepCount}-s{options.Seed}";
	}

	public IReadOnlyList<ScenarioProps> BuildScenarios(LoadTestOptions options, WorkloadContext workload,
		DiagnosticsMonitor? monitor)
	{
		var stepMinutes = options.Pilot ? Math.Min(2, options.StepMinutes) : options.StepMinutes;
		var stepHold = TimeSpan.FromMinutes(Math.Max(1, stepMinutes));
		var stepCount = Math.Max(1, options.StepCount);
		var stepDelta = Math.Max(10, options.StepDelta);
		var rampStep = options.RampDuration(stepDelta);

		var allocations = Enumerable.Range(1, stepCount)
			.Select(step => PersonaAllocation.ForSessions(step * stepDelta))
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
					sims.Add(Simulation.KeepConstant(copies, stepHold));
				}

				sims.Add(Simulation.RampingConstant(0, rampStep));
				return [.. sims];
			},
			pick =>
			{
				var sims = new List<LoadSimulation>();
				foreach (var alloc in allocations)
					sims.Add(Simulation.Inject(pick(alloc), TimeSpan.FromMinutes(1), rampStep + stepHold));
				return [.. sims];
			},
			enforceSlo: true,
			warmUp: TimeSpan.FromSeconds(15));
	}
}
