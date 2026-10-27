using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Common.Postgres;

/// <summary>Runs reads against the database alongside writes, each read on a context of its own.</summary>
/// <param name="database">The database used to create read contexts.</param>
public class DatabaseReader(Database database)
{
	// A fixed number of reads at once, so a burst cannot take every pooled connection; tune if reads queue up.
	private readonly SemaphoreSlim _readSlots = new(Environment.ProcessorCount * 2);

	/// <summary>Runs a query on a context of its own, alongside other reads; it sees what is committed.</summary>
	/// <returns>The result of the query.</returns>
	public virtual async Task<T> ReadAsync<TContext, T>(Func<TContext, Task<T>> query,
		CancellationToken cancellationToken = default) where TContext : DbContext, IDatabaseContext<TContext>
	{
		await _readSlots.WaitAsync(cancellationToken);
		try
		{
			await using var context = database.CreateConfirmedContext<TContext>();
			return await query(context);
		}
		finally
		{
			_readSlots.Release();
		}
	}
}