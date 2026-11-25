using Basil.Tests.Shared.Environment.Monitor;
using Basil.Tests.Shared.Workload.Personas;
using NBomber.Contracts;
using NBomber.CSharp;

namespace Basil.Tests.LoadTests.Spike;

/// <summary>Spike test: background load while every tournament room synchronizes on the shared tournament clock and starts at the exact same instant, then returns to background.</summary>
public sealed class SpikeTest : ILoadTest
{
	public string Name => "spike";

	public string ReportName(LoadTestOptions options)
	{
		return $"spike-n{options.TargetSessions}-s{options.Seed}";
	}

	public IReadOnlyList<ScenarioProps> BuildScenarios(LoadTestOptions options, WorkloadContext workload,
		DiagnosticsMonitor? monitor)
	{
		var bgAlloc = PersonaAllocation.ForSessions(Math.Max(10, options.TargetSessions / 4));
		var spikeAlloc = PersonaAllocation.ForSessions(options.TargetSessions);
		var preSpike = options.RampDuration(options.TargetSessions);
		var spikeHold = TimeSpan.FromMinutes(2);
		var postSpike = TimeSpan.FromMinutes(2);

		return ScenarioCatalog.BuildFullSuite(
			workload,
			monitor,
			pick =>
			{
				var bg = pick(bgAlloc);
				var peak = pick(spikeAlloc);
				return
				[
					Simulation.RampingConstant(peak, preSpike),
					Simulation.KeepConstant(peak, spikeHold),
					Simulation.RampingConstant(bg, TimeSpan.FromSeconds(15)),
					Simulation.KeepConstant(bg, postSpike),
					Simulation.RampingConstant(0, TimeSpan.FromSeconds(15))
				];
			},
			pick =>
			[
				Simulation.Inject(pick(bgAlloc), TimeSpan.FromMinutes(1), preSpike),
				Simulation.Inject(pick(spikeAlloc), TimeSpan.FromMinutes(1), spikeHold),
				Simulation.Inject(pick(bgAlloc), TimeSpan.FromMinutes(1), postSpike + TimeSpan.FromSeconds(30))
			],
			enforceSlo: false,
			warmUp: TimeSpan.Zero,
			matchRoomConfig: new MatchRoomConfig(MinPlayers: 4, MaxPlayers: 4, RoundsPerCycle: 1,
				TourneyClusters: 1, SpectatorsPerRoom: 1, OverlayViewersPerRoom: 2));
	}
}
