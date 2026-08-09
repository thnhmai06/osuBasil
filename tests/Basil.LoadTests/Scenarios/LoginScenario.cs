using Basil.LoadTests.Client;
using Basil.LoadTests.Configuration;
using Basil.LoadTests.Models;
using NBomber.Contracts;
using NBomber.CSharp;

namespace Basil.LoadTests.Scenarios;

/// <summary>
///     Login round-trip benchmark: each virtual user repeatedly logs in and logs out for the
///     configured duration, at each configured concurrency level. The resulting throughput is
///     "login round-trips per second sustained at N concurrent clients" — a steady-state figure, not a
///     thundering-herd burst of N simultaneous first-time logins. Both are legitimate measurements of
///     different things; the report states which one this is.
/// </summary>
/// <remarks>
///     A round trip is login → logout → next login. The server holds no logout grace, so every logout
///     closes its session immediately, and the measured round trip is the raw login+logout latency. A
///     canceled or failed iteration still closes its session from disposal; a session that survives
///     its iteration blocks that account's next login for up to the 10s with <c>user-already-logged-in</c>,
///     so leftovers serialize into the reported failures rather than the latency.
/// </remarks>
public sealed class LoginScenario : IBasilScenario
{
	public string Id => "login";

	public IReadOnlyList<ScenarioProps> Build(BasilScenarioContext context)
	{
		var settings = context.GetScenarioSettings<LoginSettings>(Id);
		if (!settings.Enabled) return [];

		var clientFactory = context.ClientFactory;
		var props = new List<ScenarioProps>();

		foreach (var n in settings.ConcurrentUsers)
		{
			var accounts = context.Accounts.Take(n).ToArray();
			if (accounts.Length < n)
				throw new InvalidOperationException(
					$"'{Id}' at concurrency {n} needs {n} seeded accounts but only {accounts.Length} exist; " +
					"increase Accounts:Count.");

			props.Add(Scenario.Create($"{Id}_{n}", async ctx =>
				{
					try
					{
						var account = accounts[ctx.ScenarioInfo.InstanceNumber % accounts.Length];
						await using var client = new BanchoClient(clientFactory, account);

						var outcome = await client.LoginAsync(ctx.ScenarioCancellationToken);
						if (!outcome.Success) return Response.Fail(message: outcome.FailureReason);

						await client.LogoutAsync(ctx.ScenarioCancellationToken);
						return Response.Ok(statusCode: "200");
					}
					catch (Exception ex) when (ex is not OperationCanceledException ||
					                           !ctx.ScenarioCancellationToken.IsCancellationRequested)
					{
						return Response.Fail(statusCode: ex.GetType().Name, message: ex.Message);
					}
				})
				.WithLoadSimulations(Simulation.KeepConstant(n, settings.Duration))
				.WithWarmUpDuration(settings.WarmUp)
				.WithMaxFailCount(settings.MaxFailCount));
		}

		return props;
	}

	/// <summary>
	///     Logs every account in once so the measured run hits <c>BCryptPasswordHasher</c>'s per-hash
	///     verify cache instead of paying full bcrypt cost on every login. Called once before any
	///     concurrency level runs, since the cache is process-lifetime and shared across levels.
	/// </summary>
	/// <remarks>
	///     Warm-up deliberately does <em>not</em> log out: its purpose is only the bcrypt-verify cache.
	///     Sessions are left live, and the caller must settle for at least 10s
	///     (<see cref="LoginSettings.PostWarmupSettleSeconds" />) before the measured scenario starts, so
	///     each account's stale warm-up session is old enough that its first measured login evicts it
	///     instead of being rejected.
	/// </remarks>
	public static async Task WarmBcryptCacheAsync(IReadOnlyList<LoadAccount> accounts,
		BasilHttpClientFactory clientFactory, Action<string> logInfo, CancellationToken cancellationToken = default)
	{
		var failures = 0;
		for (var i = 0; i < accounts.Count; i++)
		{
			try
			{
				await using var client = new BanchoClient(clientFactory, accounts[i]);
				var outcome = await client.LoginAsync(cancellationToken);
				if (!outcome.Success) failures++;
			}
			catch (Exception ex) when (ex is not OperationCanceledException ||
			                           !cancellationToken.IsCancellationRequested)
			{
				// A single flaky connection among thousands of sequential warm-up calls must never
				// abort the whole run — count it as a failure and move on.
				failures++;
			}

			if ((i + 1) % 10 == 0 || i == accounts.Count - 1)
				logInfo($"Warmed {i + 1}/{accounts.Count} account(s)...");
		}

		logInfo($"Warmed bcrypt-verify cache for {accounts.Count} account(s); {failures} failed to log in.");
	}
}