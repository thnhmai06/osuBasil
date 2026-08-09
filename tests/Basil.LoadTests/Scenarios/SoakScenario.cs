using System.Collections.Concurrent;
using Basil.LoadTests.Client;
using Basil.LoadTests.Configuration;
using Basil.Protocol;
using Basil.Protocol.Multiplayer;
using NBomber.Contracts;
using NBomber.CSharp;

namespace Basil.LoadTests.Scenarios;

/// <summary>
///     Long-running weighted mix of the Phase 2 workloads, held for hours rather than minutes, watching
///     for memory/thread/handle growth via <see cref="Analysis.SoakAnalyzer" /> after the run.
/// </summary>
/// <remarks>
///     Each virtual user holds one persistent login and rolls a weighted action every iteration rather
///     than being permanently assigned a role — over the run's duration this converges to the
///     configured mix without needing cross-instance room coordination. The "multiplayer" bucket is a
///     solo create→part cycle (exercises the same packet/DB paths without needing other virtual users
///     to synchronize into the same room over many hours unattended) rather than <see cref="MultiplayerScenario" />'s
///     full multi-player room lifecycle.
/// </remarks>
public sealed class SoakScenario : IBasilScenario
{
	public string Id => "soak";

	public IReadOnlyList<ScenarioProps> Build(BasilScenarioContext context)
	{
		var settings = context.GetScenarioSettings<SoakSettings>(Id);
		if (!settings.Enabled || settings.ConcurrentUsers.Length == 0) return [];

		var clientFactory = context.ClientFactory;
		var accounts = context.Accounts;
		var pollInterval = context.Profile.Client.PollInterval;
		var weightedActions = BuildWeightedActions(settings.Weights);
		var clients = new ConcurrentBag<BanchoClient>();

		var n = settings.ConcurrentUsers[0];
		if (accounts.Count < n)
			throw new InvalidOperationException(
				$"'{Id}' needs {n} seeded accounts but only {accounts.Count} exist; increase Accounts:Count.");

		var scenario = Scenario.Create($"{Id}_{n}", async ctx =>
			{
				try
				{
					var account = accounts[ctx.ScenarioInfo.InstanceNumber % accounts.Count];

					if (!ctx.ScenarioInstanceData.TryGetValue("client", out var stored))
					{
						var newClient = new BanchoClient(clientFactory, account);
						var outcome = await newClient.LoginAsync(ctx.ScenarioCancellationToken);
						if (!outcome.Success)
							return Response.Fail(statusCode: outcome.FailureReason ?? "unknown-failure");

						clients.Add(newClient);
						ctx.ScenarioInstanceData["client"] = newClient;
						return Response.Ok(statusCode: "login");
					}

					var client = (BanchoClient)stored;
					var action = weightedActions.Count == 0
						? "idle"
						: weightedActions[Random.Shared.Next(weightedActions.Count)];

					await Task.Delay(pollInterval, ctx.ScenarioCancellationToken);

					switch (action)
					{
						case "chat":
							client.Send(ClientPacketWriter.SendPublicMessage(
								new BanchoMessage(account.Name, $"soak:{DateTimeOffset.UtcNow.Ticks}", "#osu",
									account.UserId ?? 0)));
							await client.PollAsync(ctx.ScenarioCancellationToken);
							return Response.Ok(statusCode: "chat");

						case "multiplayer":
							var match = new MatchPacket(0, false, 0, $"soak-{ctx.ScenarioInfo.InstanceNumber}", "",
								"", 0, "",
								[.. Enumerable.Range(0, 16).Select(_ => new MatchSlotPacket(0, 0, 0, null))],
								account.UserId ?? 0, 0, 0, 0, false, 0);
							client.Send(ClientPacketWriter.CreateMatch(match));
							await client.PollAsync(ctx.ScenarioCancellationToken);
							client.Send(ClientPacketWriter.PartMatch());
							await client.PollAsync(ctx.ScenarioCancellationToken);
							return Response.Ok(statusCode: "multiplayer");

						case "api":
							using (var http = clientFactory.CreateClient())
							{
								var uri = clientFactory.BuildUri("api", $"/users/{account.UserId ?? 0}");
								using var response = await http.GetAsync(uri, ctx.ScenarioCancellationToken);
								return response.IsSuccessStatusCode
									? Response.Ok(statusCode: "200")
									: Response.Fail(statusCode: ((int)response.StatusCode).ToString());
							}

						default:
							await client.PollAsync(ctx.ScenarioCancellationToken);
							return Response.Ok(statusCode: "idle");
					}
				}
				catch (Exception ex) when (ex is not OperationCanceledException ||
				                            !ctx.ScenarioCancellationToken.IsCancellationRequested)
				{
					// A 12-24h unattended run must never let one transient failure propagate as an
					// unhandled exception — always resolve to a counted failure and keep going.
					return Response.Fail(statusCode: ex.GetType().Name);
				}
			})
			.WithLoadSimulations(Simulation.KeepConstant(n, settings.Duration))
			.WithWarmUpDuration(settings.WarmUp)
			.WithMaxFailCount(int.MaxValue)
			.WithClean(async _ =>
			{
				foreach (var client in clients) await client.DisposeAsync();
			});

		// NBomberRunner.WithReportingInterval (applied per-run, not per-scenario) is set from
		// this same SoakSettings.ReportingInterval by Program.cs when it runs this scenario, so a
		// multi-hour run streams interim stats instead of reporting only once at the end.
		return [scenario];
	}

	private static List<string> BuildWeightedActions(IReadOnlyDictionary<string, int> weights)
	{
		var list = new List<string>();
		foreach (var (action, weight) in weights)
			for (var i = 0; i < Math.Max(1, weight); i++)
				list.Add(action);

		return list;
	}
}
