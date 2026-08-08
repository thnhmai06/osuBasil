using Basil.LoadTests.Client;
using Basil.LoadTests.Configuration;
using Basil.LoadTests.Helpers;
using Basil.LoadTests.Models;

namespace Basil.LoadTests.Hosting;

/// <summary>
///     Builds the load-test account pool and seeds it via the admin API when it does not already exist.
///     Seeding is deliberately never idempotent-per-account (no <c>GET /users/{name}</c> existence
///     probe): the database snapshot's presence is the idempotency check, decided once by the caller
///     before this class does any work, so a fresh checkout pays the ~bcrypt cost of seeding exactly once.
/// </summary>
public sealed class AccountSeeder(BasilApiClient apiClient)
{
	/// <summary>Builds the deterministic account list for a pool of the given size, without contacting the server.</summary>
	public static IReadOnlyList<LoadAccount> BuildAccounts(AccountPoolSettings settings)
	{
		var digits = Math.Max(5, settings.Count.ToString().Length);
		var accounts = new List<LoadAccount>(settings.Count);
		for (var i = 0; i < settings.Count; i++)
		{
			var name = $"{settings.NamePrefix}{i.ToString().PadLeft(digits, '0')}";
			accounts.Add(new LoadAccount
			{
				Index = i,
				Name = name,
				Password = settings.Password,
				PasswordMd5 = Md5Hex.Of(settings.Password)
			});
		}

		return accounts;
	}

	/// <summary>
	///     Creates every account in <paramref name="accounts" /> via the admin API, filling in each
	///     <see cref="LoadAccount.UserId" />. Uses the conflict-tolerant <c>EnsureUserAsync</c> so this
	///     is also safe to call against a server whose database could not be snapshotted (accounts from
	///     a previous run may already exist).
	/// </summary>
	/// <returns>The accounts that failed to seed, retried once each; still-failing accounts are logged, not thrown.</returns>
	public async Task<int> SeedAsync(IReadOnlyList<LoadAccount> accounts, Action<string>? logWarning = null,
		CancellationToken cancellationToken = default)
	{
		var failures = 0;
		foreach (var account in accounts)
		{
			try
			{
				account.UserId =
					await apiClient.EnsureUserAsync(account.Name, account.Password, "vn", cancellationToken);
			}
			catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
			{
				// A single flaky connection among thousands of sequential seed calls must never
				// abort the whole run. One retry covers a transient hiccup; a second failure is logged.
				try
				{
					account.UserId =
						await apiClient.EnsureUserAsync(account.Name, account.Password, "vn", cancellationToken);
				}
				catch (Exception retryEx) when (retryEx is not OperationCanceledException ||
				                                 !cancellationToken.IsCancellationRequested)
				{
					failures++;
					logWarning?.Invoke($"Failed to seed account '{account.Name}': {retryEx.Message}");
				}
			}
		}

		return failures;
	}
}
