using Basil.Tests.Shared.Environment.Monitor;
using Basil.Tests.Shared.Workload.Personas;
using NBomber.Contracts;
using NBomber.CSharp;

namespace Basil.Tests.LoadTests.Smoke;

/// <summary>T8 smoke run: two 2v2 tournament rooms (one IRC referee, one REST referee), 20 free players and edge cases for two minutes with zero errors.</summary>
public sealed class SmokeTest : ILoadTest
{
	public string Name => "smoke";

	public string ReportName(LoadTestOptions options)
	{
		return $"smoke-s{options.Seed}";
	}

	public IReadOnlyList<ScenarioProps> BuildScenarios(LoadTestOptions options, WorkloadContext workload,
		DiagnosticsMonitor? monitor)
	{
		var duration = options.SteadyDuration == TimeSpan.FromMinutes(10)
			? TimeSpan.FromMinutes(2)
			: options.SteadyDuration;
		var ramp = TimeSpan.FromSeconds(15);

		var matchRoom = new MatchRoomPersona(workload, new MatchRoomConfig(
			MinPlayers: 4, MaxPlayers: 4, RoundsPerCycle: 2,
			TourneyClusters: 0, SpectatorsPerRoom: 1, OverlayViewersPerRoom: 2));
		var freePlayer = new FreePlayerPersona(workload);
		var edgeCases = new EdgeCasesPersona(workload);

		var roomsScenario = Scenario.Create("match_room", async ctx =>
			{
				await matchRoom.RunCycleAsync(ctx);
				return Response.Ok();
			})
			.WithoutWarmUp()
			.WithLoadSimulations(
				Simulation.RampingConstant(2, ramp),
				Simulation.KeepConstant(2, duration),
				Simulation.RampingConstant(0, TimeSpan.FromSeconds(10)))
			.WithThresholds(SloThresholds.ForScenario(includePoll: true, includeSubmit: true, includeRest: true,
				includeLogin: true, includeServerCpu: monitor is not null, maxErrorPercent: 0.01));

		if (monitor is not null)
		{
			roomsScenario = roomsScenario.WithInit(ctx =>
			{
				monitor.RegisterMetrics(ctx);
				monitor.Start();
				return Task.CompletedTask;
			});
		}

		var freeScenario = Scenario.Create("free_player", async ctx =>
			{
				await freePlayer.RunCycleAsync(ctx);
				return Response.Ok();
			})
			.WithoutWarmUp()
			.WithLoadSimulations(
				Simulation.RampingConstant(20, ramp),
				Simulation.KeepConstant(20, duration),
				Simulation.RampingConstant(0, TimeSpan.FromSeconds(10)))
			.WithThresholds(SloThresholds.ForScenario(includePoll: true, includeSubmit: true, includeGetScores: true,
				includeLogin: true, maxErrorPercent: 0.01));

		var edgeScenario = Scenario.Create("edge_cases", async ctx =>
			{
				await edgeCases.RunCycleAsync(ctx);
				return Response.Ok();
			})
			.WithoutWarmUp()
			.WithLoadSimulations(Simulation.Inject(2, TimeSpan.FromMinutes(1), ramp + duration))
			.WithThresholds(SloThresholds.ForScenario(maxErrorPercent: 0.01));

		return [roomsScenario, freeScenario, edgeScenario];
	}
}
