using Basil.Tests.Shared.Environment.Monitor;
using Basil.Tests.Shared.Workload.Personas;
using NBomber.Contracts;
using NBomber.CSharp;

namespace Basil.Tests.LoadTests.Load;

/// <summary>Closed-model Load test: warm-up, bcrypt-safe ramp, steady N sessions, ramp-down, with full SLO thresholds.</summary>
public sealed class LoadTest : ILoadTest
{
	private static readonly TimeSpan WarmUp = TimeSpan.FromSeconds(30);

	public string Name => "load";

	public string ReportName(LoadTestOptions options)
	{
		return $"load-n{options.TargetSessions}-s{options.Seed}";
	}

	public RunPlan Plan(LoadTestOptions options)
	{
		var (ramp, steady, rampDown) = Shape(options);
		return RunPlan.Create(WarmUp, rampDown, ("steady", options.TargetSessions, ramp + steady));
	}

	public IReadOnlyList<ScenarioProps> BuildScenarios(LoadTestOptions options, WorkloadContext workload,
		DiagnosticsMonitor? monitor)
	{
		var alloc = PersonaAllocation.ForSessions(options.TargetSessions);
		var (ramp, steady, rampDown) = Shape(options);

		return ScenarioCatalog.BuildFullSuite(
			workload,
			monitor,
			pick =>
			{
				var copies = pick(alloc);
				return
				[
					Simulation.RampingConstant(copies, ramp),
					Simulation.KeepConstant(copies, steady),
					Simulation.RampingConstant(0, rampDown)
				];
			},
			pick =>
			{
				var rate = pick(alloc);
				return [Injection.PerMinute(rate, ramp + steady + rampDown)];
			},
			enforceSlo: true,
			warmUp: WarmUp);
	}

	private static (TimeSpan Ramp, TimeSpan Steady, TimeSpan RampDown) Shape(LoadTestOptions options)
	{
		var ramp = options.RampDuration(options.TargetSessions);
		return (ramp, options.SteadyDuration, TimeSpan.FromSeconds(Math.Max(15, ramp.TotalSeconds / 2)));
	}
}
