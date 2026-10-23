namespace Basil.Infrastructure.Persistence.Postgres;

/// <summary>Runs reads against the database alongside writes, each read on a context of its own.</summary>
internal class DatabaseReader(Database database)
{
	// ponytail: a fixed number of reads at once, so a burst cannot take every pooled connection; tune if reads queue up.
	private readonly SemaphoreSlim _readSlots = new(Environment.ProcessorCount * 2);

	/// <summary>Runs a query on a context of its own, alongside other reads; it sees what is committed.</summary>
	/// <returns>The result of the query.</returns>
	public virtual async Task<T> ReadAsync<T>(Func<BasilDbContext, Task<T>> query,
		CancellationToken cancellationToken = default)
	{
		await _readSlots.WaitAsync(cancellationToken);
		try
		{
			await using var context = database.CreateSynchronousContext();
			return await query(context);
		}
		finally
		{
			_readSlots.Release();
		}
	}
}