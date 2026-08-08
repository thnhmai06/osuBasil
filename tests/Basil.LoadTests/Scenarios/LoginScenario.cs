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
///     A round trip is login → (hold ≥1s, enforced by <see cref="Client.BanchoClient.LogoutAsync" />
///     against the server's logout grace) → logout → next login. The server rejects a relogin within
///     the 10s of a still-live session, so a logout that never takes effect (sent inside the grace period)
///     would make every subsequent login fail with <c>user-already-logged-in</c>; the hold guarantees
///     the logout is honored. The measured latency therefore includes the hold — it is a real,
///     server-enforced part of the round trip, and bounds sustained throughput to roughly one round
///     trip per second per client.
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
						using var client = new BanchoClient(clientFactory, account, settings.MinSessionHold);

						var loggedIn = false;
						try
						{
							var outcome = await client.LoginAsync(ctx.ScenarioCancellationToken);
							if (!outcome.Success)
								return Response.Fail(statusCode: $"{outcome.FailureReason}::{account.Name}::inst{ctx.ScenarioInfo.InstanceNumber}");
							loggedIn = true;

							await client.LogoutAsync(ctx.ScenarioCancellationToken);
							return Response.Ok(statusCode: "200");
						}
						finally
						{
							// A cancelled iteration (e.g. warm-up ending mid-hold) must still close the
							// session, or the account's next login fails with user-already-logged-in.
							if (loggedIn)
								await client.LogoutIgnoringCancellationAsync();
						}
					}
					catch (Exception ex) when (ex is not OperationCanceledException ||
					                            !ctx.ScenarioCancellationToken.IsCancellationRequested)
					{
						return Response.Fail(statusCode: ex.GetType().Name);
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
	///     Warm-up deliberately does <em>not</em> log out: a logout within the server's 1s post-login
	///     grace is ignored anyway, and logging out would take the full min-hold per account. The
	///     sessions are left live, and the caller must settle for the at least 10s (see
	///     <see cref="LoginSettings.PostWarmupSettleSeconds" />) before the measured scenario starts, so
	///     each account's first measured login evicts its stale session instead of being rejected.
	/// </remarks>
	public static async Task WarmBcryptCacheAsync(IReadOnlyList<LoadAccount> accounts,
		BasilHttpClientFactory clientFactory, Action<string> logInfo, CancellationToken cancellationToken = default)
	{
		var failures = 0;
		for (var i = 0; i < accounts.Count; i++)
		{
			try
			{
				using var client = new BanchoClient(clientFactory, accounts[i]);
				var outcome = await client.LoginAsync(cancellationToken);
				if (!outcome.Success) failures++;
			}
			catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
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
