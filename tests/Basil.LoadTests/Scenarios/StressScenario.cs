using System.Diagnostics;
using System.Net.Sockets;
using Basil.LoadTests.Analysis;
using Basil.LoadTests.Client;
using Basil.LoadTests.Configuration;
using NBomber.Contracts;
using NBomber.CSharp;

namespace Basil.LoadTests.Scenarios;

/// <summary>
///     Progressive ramp-up to failure: chains ramp-then-hold steps across every configured concurrency
///     level with no gap between them, driven by repeated login round-trips (the most resource-intensive
///     single operation identified — bcrypt plus two SQLite writes per call). <see cref="StressSettings.MaxFailCount" />
///     is set far above NBomber's default 5000, so the run keeps collecting metrics through failures
///     instead of stopping at the first wave of them.
/// </summary>
public sealed class StressScenario : IBasilScenario
{
	public string Id => "stress";

	public IReadOnlyList<ScenarioProps> Build(BasilScenarioContext context)
	{
		var settings = context.GetScenarioSettings<StressSettings>(Id);
		if (!settings.Enabled || settings.ConcurrentUsers.Length == 0) return [];

		var clientFactory = context.ClientFactory;
		var accounts = context.Accounts;
		var reportFolder = context.ReportFolder;
		var eventLog = new StressEventLog();

		if (accounts.Count < settings.ConcurrentUsers.Max())
			throw new InvalidOperationException(
				$"'{Id}' needs at least {settings.ConcurrentUsers.Max()} seeded accounts but only " +
				$"{accounts.Count} exist; increase Accounts:Count.");

		var simulations = new List<LoadSimulation>();
		foreach (var level in settings.ConcurrentUsers)
		{
			simulations.Add(Simulation.RampingConstant(level, settings.Ramp));
			simulations.Add(Simulation.KeepConstant(level, settings.Hold));
		}

		simulations.Add(Simulation.RampingConstant(0, settings.Ramp));

		var boundaries = ComputeStepBoundaries(settings);
		var clock = Stopwatch.StartNew();

		var scenario = Scenario.Create(Id, async ctx =>
			{
				var account = accounts[ctx.ScenarioInfo.InstanceNumber % accounts.Count];
				var currentLevel = ResolveCurrentLevelLabel(boundaries, clock.Elapsed);

				try
				{
					await using var client = new BanchoClient(clientFactory, account);
					var outcome = await client.LoginAsync(ctx.ScenarioCancellationToken);
					if (!outcome.Success)
					{
						eventLog.RecordServerError(currentLevel, outcome.FailureReason ?? "unknown-failure");
						return Response.Fail(message: outcome.FailureReason ?? "unknown-failure");
					}

					await client.LogoutAsync(ctx.ScenarioCancellationToken);
					return Response.Ok(statusCode: "200");
				}
				catch (SocketException socketEx)
				{
					eventLog.RecordConnectionFailure(currentLevel, socketEx.SocketErrorCode.ToString());
					return Response.Fail(statusCode: "connection-failure");
				}
				catch (HttpRequestException httpEx)
				{
					eventLog.RecordConnectionFailure(currentLevel, httpEx.Message);
					return Response.Fail(statusCode: "http-error");
				}
				catch (TaskCanceledException) when (!ctx.ScenarioCancellationToken.IsCancellationRequested)
				{
					eventLog.RecordTimeout(currentLevel);
					return Response.Fail(statusCode: "timeout");
				}
			})
			.WithLoadSimulations([.. simulations])
			.WithoutWarmUp()
			.WithMaxFailCount(settings.MaxFailCount)
			.WithClean(_ =>
			{
				eventLog.WriteReport(reportFolder);
				return Task.CompletedTask;
			});

		return [scenario];
	}

	/// <summary>
	///     Maps elapsed time to the schedule phase the ramp should be in — an approximation of NBomber's
	///     own internal scheduling, good enough to label which failure class showed up during which
	///     phase. Every phase is tracked explicitly (ramp-up toward a level, holding at a level, or the
	///     final ramp-down to 0) rather than only the target level, so a failure caught mid-ramp is never
	///     misattributed to a level the run wasn't actually sustaining yet (or any longer) — see
	///     2026-08-14's verify-stress run, where failures during the final ramp-down were reported as
	///     "concurrency level 100" even though the run was already winding down from it.
	/// </summary>
	private static List<(TimeSpan End, string Label)> ComputeStepBoundaries(StressSettings settings)
	{
		var boundaries = new List<(TimeSpan End, string Label)>();
		var elapsed = TimeSpan.Zero;
		var previousLevel = 0;
		foreach (var level in settings.ConcurrentUsers)
		{
			elapsed += settings.Ramp;
			boundaries.Add((elapsed, $"ramp-up {previousLevel}→{level}"));
			elapsed += settings.Hold;
			boundaries.Add((elapsed, level.ToString()));
			previousLevel = level;
		}

		elapsed += settings.Ramp;
		boundaries.Add((elapsed, $"ramp-down {previousLevel}→0"));

		return boundaries;
	}

	private static string ResolveCurrentLevelLabel(List<(TimeSpan End, string Label)> boundaries, TimeSpan elapsed)
	{
		foreach (var (end, label) in boundaries)
			if (elapsed <= end)
				return label;

		return boundaries.Count > 0 ? boundaries[^1].Label : "0";
	}
}