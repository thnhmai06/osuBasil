using Basil.Tests.Shared.Environment.Monitor;
using Basil.Tests.Shared.Workload.Personas;
using NBomber.Contracts;
using NBomber.CSharp;

namespace Basil.Tests.LoadTests.Capacity;

/// <summary>Capacity test: adds <c>+ΔN</c> every 5 minutes until the SLO breaks; the pilot is a shorter Capacity run.</summary>
public sealed class CapacityTest : ILoadTest
{
	private static readonly TimeSpan WarmUp = TimeSpan.FromSeconds(15);

	public string Name => "capacity";

	public string ReportName(LoadTestOptions options)
	{
		var prefix = options.Pilot ? "pilot" : "capacity";
		return $"{prefix}-d{options.StepDelta}-x{options.StepCount}-s{options.Seed}";
	}

	public RunPlan Plan(LoadTestOptions options)
	{
		var (stepHold, stepCount, stepDelta, rampStep) = Shape(options);
		return RunPlan.Create(WarmUp, rampStep,
			[.. Enumerable.Range(1, stepCount).Select(step => ($"step {step}", step * stepDelta, rampStep + stepHold))]);
	}

	public IReadOnlyList<ScenarioProps> BuildScenarios(LoadTestOptions options, WorkloadContext workload,
		DiagnosticsMonitor? monitor)
	{
		var (stepHold, stepCount, stepDelta, rampStep) = Shape(options);

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
					sims.Add(Injection.PerMinute(pick(alloc), rampStep + stepHold));
				return [.. sims];
			},
			enforceSlo: true,
			warmUp: WarmUp);
	}

	private static (TimeSpan StepHold, int StepCount, int StepDelta, TimeSpan RampStep) Shape(LoadTestOptions options)
	{
		var stepMinutes = options.Pilot ? Math.Min(2, options.StepMinutes) : options.StepMinutes;
		var stepDelta = Math.Max(10, options.StepDelta);
		return (TimeSpan.FromMinutes(Math.Max(1, stepMinutes)), Math.Max(1, options.StepCount), stepDelta,
			options.RampDuration(stepDelta));
	}
}
