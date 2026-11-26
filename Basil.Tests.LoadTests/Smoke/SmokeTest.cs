using Basil.Tests.Shared.Environment.Monitor;
using Basil.Tests.Shared.Workload.Personas;
using NBomber.Contracts;
using NBomber.CSharp;

namespace Basil.Tests.LoadTests.Smoke;

/// <summary>
///     T8 smoke run: two 2v2 tournament rooms (one IRC referee, one REST referee), 20 free players and edge cases for
///     two minutes with zero errors.
/// </summary>
public sealed class SmokeTest : ILoadTest
{
	/// <summary>The number of rooms the smoke run plays at once; the server-side checks expect that many matches with rounds.</summary>
	public const int Rooms = 2;

	private const int FreePlayers = 20;
	private static readonly TimeSpan Ramp = TimeSpan.FromSeconds(15);
	private static readonly TimeSpan RampDown = TimeSpan.FromSeconds(10);

	private static readonly MatchRoomConfig RoomConfig = new(
		4, 4, 2,
		0, 1, 2);

	public string Name => "smoke";

	public string ReportName(LoadTestOptions options)
	{
		return $"smoke-s{options.Seed}";
	}

	public RunPlan Plan(LoadTestOptions options)
	{
		var sessions = (int)Math.Round(Rooms * PersonaAllocation.SessionsPerMatchRoom(RoomConfig) + FreePlayers);
		return RunPlan.Create(TimeSpan.Zero, RampDown, ("smoke", sessions, Ramp, Ramp + Duration(options)));
	}

	public IReadOnlyList<ScenarioProps> BuildScenarios(LoadTestOptions options, WorkloadContext workload,
		DiagnosticsMonitor? monitor)
	{
		var duration = Duration(options);

		var matchRoom = new MatchRoomPersona(workload, RoomConfig);
		var freePlayer = new FreePlayerPersona(workload);
		var edgeCases = new EdgeCasesPersona(workload);

		var roomsScenario = Scenario.Create("match_room", async ctx =>
			{
				await matchRoom.RunCycleAsync(ctx);
				return Response.Ok();
			})
			.WithRestartIterationOnFail(false)
			.WithoutWarmUp()
			.WithClean(_ => matchRoom.ShutdownAsync())
			.WithLoadSimulations(
				Simulation.RampingConstant(Rooms, Ramp),
				Simulation.KeepConstant(Rooms, duration),
				Simulation.RampingConstant(0, RampDown))
			.WithThresholds(SloThresholds.ForScenario(true, true, includeRest: true,
				includeLogin: true, maxErrorPercent: 0.01));

		if (monitor is not null)
			roomsScenario = roomsScenario.WithInit(ctx =>
			{
				monitor.RegisterMetrics(ctx);
				monitor.Start();
				return Task.CompletedTask;
			});

		var freeScenario = Scenario.Create("free_player", async ctx =>
			{
				await freePlayer.RunCycleAsync(ctx);
				return Response.Ok();
			})
			.WithRestartIterationOnFail(false)
			.WithoutWarmUp()
			.WithClean(_ => freePlayer.ShutdownAsync())
			.WithLoadSimulations(
				Simulation.RampingConstant(FreePlayers, Ramp),
				Simulation.KeepConstant(FreePlayers, duration),
				Simulation.RampingConstant(0, RampDown))
			.WithThresholds(SloThresholds.ForScenario(true, true, true,
				includeLogin: true, maxErrorPercent: 0.01));

		var edgeScenario = Scenario.Create("edge_cases", async ctx =>
			{
				await edgeCases.RunCycleAsync(ctx);
				return Response.Ok();
			})
			.WithRestartIterationOnFail(false)
			.WithoutWarmUp()
			.WithClean(_ => EdgeCasesPersona.ShutdownAsync())
			.WithLoadSimulations(Injection.PerMinute(2, Ramp + duration))
			.WithThresholds(SloThresholds.ForScenario(maxErrorPercent: 0.01));

		return [roomsScenario, freeScenario, edgeScenario];
	}

	private static TimeSpan Duration(LoadTestOptions options)
	{
		return options.SteadyDuration == TimeSpan.FromMinutes(10) ? TimeSpan.FromMinutes(2) : options.SteadyDuration;
	}
}