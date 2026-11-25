using System.Collections.Concurrent;
using Basil.Tests.Shared.Clients.Stable;

namespace Basil.Tests.Shared.Workload.World;

/// <summary>Hands out seeded accounts without concurrent double-use and keeps one fixed <see cref="StableIdentity"/> per account.</summary>
public sealed class AccountPool
{
	private readonly ConcurrentDictionary<AccountRole, ConcurrentQueue<AccountEntry>> _available = new();
	private readonly ConcurrentDictionary<(string Name, bool Tourney), StableIdentity> _identities = new();
	private readonly Dictionary<AccountRole, int> _totals = [];

	/// <summary>Creates a pool from the loaded accounts.</summary>
	public AccountPool(IReadOnlyList<AccountEntry> accounts)
	{
		foreach (var role in Enum.GetValues<AccountRole>())
		{
			_available[role] = new ConcurrentQueue<AccountEntry>();
			_totals[role] = 0;
		}

		foreach (var account in accounts)
		{
			_available[account.Role].Enqueue(account);
			_totals[account.Role]++;
		}
	}

	/// <summary>Gets the total number of accounts loaded for a role.</summary>
	public int CountOf(AccountRole role)
	{
		return _totals.GetValueOrDefault(role);
	}

	/// <summary>Checks out one account of the requested role; dispose the lease to return it.</summary>
	public bool TryAcquire(AccountRole role, out AccountLease lease)
	{
		if (_available[role].TryDequeue(out var account))
		{
			lease = new AccountLease(this, account);
			return true;
		}

		lease = default!;
		return false;
	}

	/// <summary>Checks out <paramref name="count"/> accounts of the requested role, or returns all of them if fewer are free.</summary>
	public bool TryAcquireMany(AccountRole role, int count, out List<AccountLease> leases)
	{
		leases = new List<AccountLease>(count);
		for (var i = 0; i < count; i++)
		{
			if (!TryAcquire(role, out var lease))
			{
				foreach (var acquired in leases) acquired.Dispose();
				leases.Clear();
				return false;
			}

			leases.Add(lease);
		}

		return true;
	}

	/// <summary>Gets the deterministic <see cref="StableIdentity"/> of an account; the same account always produces the same install hashes.</summary>
	public StableIdentity IdentityOf(AccountEntry account, bool tourney = false)
	{
		return _identities.GetOrAdd((account.Name, tourney),
			_ => StableIdentity.Create(account.Name, account.Password, tourney));
	}

	internal void Return(AccountEntry account)
	{
		_available[account.Role].Enqueue(account);
	}
}

/// <summary>An exclusive checkout of one account from an <see cref="AccountPool"/>.</summary>
public sealed class AccountLease : IDisposable
{
	private readonly AccountPool _pool;
	private int _returned;

	internal AccountLease(AccountPool pool, AccountEntry account)
	{
		_pool = pool;
		Account = account;
	}

	/// <summary>Gets the leased account.</summary>
	public AccountEntry Account { get; }

	/// <summary>Gets the deterministic osu! stable identity for this account.</summary>
	public StableIdentity Identity(bool tourney = false)
	{
		return _pool.IdentityOf(Account, tourney);
	}

	/// <summary>Returns the account to the pool.</summary>
	public void Dispose()
	{
		if (Interlocked.Exchange(ref _returned, 1) == 0) _pool.Return(Account);
	}
}
