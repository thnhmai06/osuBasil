using System.Collections.Concurrent;
using Basil.LoadTests.Client;
using Basil.LoadTests.Configuration;
using NBomber.Contracts;
using NBomber.CSharp;

namespace Basil.LoadTests.Scenarios;

/// <summary>
///     Connects N users, then holds them idle, polling only as often as
///     <see cref="ClientSettings.PollIntervalSeconds" /> dictates — which must stay comfortably under
///     the server's 300-second ghost-session reaper interval, or the run silently measures an empty
///     server partway through. Resource figures (memory/session, CPU, GC, threads) come from the
///     resource timeline <c>Program.cs</c> samples for the whole run duration, not from this scenario.
/// </summary>
public sealed class IdleScenario : IBasilScenario
{
	public string Id => "idle";

	public IReadOnlyList<ScenarioProps> Build(BasilScenarioContext context)
	{
		var settings = context.GetScenarioSettings<IdleSettings>(Id);
		if (!settings.Enabled) return [];

		var clientFactory = context.ClientFactory;
		var pollInterval = context.Profile.Client.PollInterval;
		var props = new List<ScenarioProps>();

		foreach (var n in settings.ConcurrentUsers)
		{
			var accounts = context.Accounts.Take(n).ToArray();
			if (accounts.Length < n)
				throw new InvalidOperationException(
					$"'{Id}' at concurrency {n} needs {n} seeded accounts but only {accounts.Length} exist; " +
					"increase Accounts:Count.");

			var clients = new ConcurrentBag<BanchoClient>();

			props.Add(Scenario.Create($"{Id}_{n}", async ctx =>
				{
					try
					{
						var account = accounts[ctx.ScenarioInfo.InstanceNumber % accounts.Length];

						if (!ctx.ScenarioInstanceData.TryGetValue("client", out var stored))
						{
							var client = new BanchoClient(clientFactory, account);
							var outcome = await client.LoginAsync(ctx.ScenarioCancellationToken);
							if (!outcome.Success)
								return Response.Fail(statusCode: outcome.FailureReason ?? "unknown-failure");

							clients.Add(client);
							ctx.ScenarioInstanceData["client"] = client;
							return Response.Ok(statusCode: "login");
						}

						await Task.Delay(pollInterval, ctx.ScenarioCancellationToken);
						var existing = (BanchoClient)stored;
						await existing.PollAsync(ctx.ScenarioCancellationToken);
						return Response.Ok(statusCode: "poll");
					}
					catch (Exception ex) when (ex is not OperationCanceledException ||
					                            !ctx.ScenarioCancellationToken.IsCancellationRequested)
					{
						return Response.Fail(statusCode: ex.GetType().Name);
					}
				})
				.WithLoadSimulations(Simulation.KeepConstant(n, settings.Duration))
				.WithWarmUpDuration(settings.WarmUp)
				.WithMaxFailCount(settings.MaxFailCount)
				.WithClean(async _ =>
				{
					foreach (var client in clients) await client.DisposeAsync();
				}));
		}

		return props;
	}
}
