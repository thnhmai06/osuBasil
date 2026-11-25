using Basil.Tests.Shared.Environment.Monitor;
using Basil.Tests.Shared.Workload.Personas;
using NBomber.Contracts;
using NBomber.CSharp;

namespace Basil.Tests.LoadTests.Load;

/// <summary>Closed-model Load test: warm-up, bcrypt-safe ramp, steady N sessions, ramp-down, with full SLO thresholds.</summary>
public sealed class LoadTest : ILoadTest
{
	public string Name => "load";

	public string ReportName(LoadTestOptions options)
	{
		return $"load-n{options.TargetSessions}-s{options.Seed}";
	}

	public IReadOnlyList<ScenarioProps> BuildScenarios(LoadTestOptions options, WorkloadContext workload,
		DiagnosticsMonitor? monitor)
	{
		var alloc = PersonaAllocation.ForSessions(options.TargetSessions);
		var ramp = options.RampDuration(options.TargetSessions);
		var steady = options.SteadyDuration;
		var rampDown = TimeSpan.FromSeconds(Math.Max(15, ramp.TotalSeconds / 2));

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
				return
				[
					Simulation.Inject(rate, TimeSpan.FromMinutes(1), ramp + steady + rampDown)
				];
			},
			enforceSlo: true,
			warmUp: TimeSpan.FromSeconds(30));
	}
}
