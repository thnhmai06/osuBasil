using Basil.Tests.Shared.Environment.Monitor;
using Basil.Tests.Shared.Workload.Personas;
using NBomber.Contracts;
using NBomber.CSharp;

namespace Basil.Tests.LoadTests.Spike;

/// <summary>
///     Spike test: background load while every tournament room synchronizes on the shared tournament clock and starts
///     at the exact same instant, then returns to background.
/// </summary>
public sealed class SpikeTest : ILoadTest
{
	private static readonly TimeSpan SpikeHold = TimeSpan.FromMinutes(2);
	private static readonly TimeSpan PostSpike = TimeSpan.FromMinutes(2);
	private static readonly TimeSpan Settle = TimeSpan.FromSeconds(15);

	private static readonly MatchRoomConfig RoomConfig = new(4, 4, 1,
		1, 1, 2);

	public string Name => "spike";

	public string ReportName(LoadTestOptions options)
	{
		return $"spike-n{options.TargetSessions}-s{options.Seed}";
	}

	public RunPlan Plan(LoadTestOptions options)
	{
		var background = BackgroundSessions(options);
		return RunPlan.Create(TimeSpan.Zero, Settle,
			("ramp", options.TargetSessions, options.RampDuration(options.TargetSessions)),
			("spike", options.TargetSessions, SpikeHold),
			("back to background", background, Settle),
			("background", background, PostSpike));
	}

	public IReadOnlyList<ScenarioProps> BuildScenarios(LoadTestOptions options, WorkloadContext workload,
		DiagnosticsMonitor? monitor)
	{
		var bgAlloc = PersonaAllocation.ForSessions(BackgroundSessions(options), RoomConfig);
		var spikeAlloc = PersonaAllocation.ForSessions(options.TargetSessions, RoomConfig);
		var preSpike = options.RampDuration(options.TargetSessions);
		var spikeHold = SpikeHold;
		var postSpike = PostSpike;

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
					Simulation.RampingConstant(bg, Settle),
					Simulation.KeepConstant(bg, postSpike),
					Simulation.RampingConstant(0, Settle)
				];
			},
			pick =>
			[
				Injection.PerMinute(pick(bgAlloc), preSpike),
				Injection.PerMinute(pick(spikeAlloc), spikeHold),
				Injection.PerMinute(pick(bgAlloc), postSpike + TimeSpan.FromSeconds(30))
			],
			false,
			TimeSpan.Zero,
			RoomConfig);
	}

	private static int BackgroundSessions(LoadTestOptions options)
	{
		return Math.Max(10, options.TargetSessions / 4);
	}
}