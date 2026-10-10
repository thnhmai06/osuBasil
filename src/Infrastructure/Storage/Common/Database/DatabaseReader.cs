using Npgsql;

namespace Basil.Infrastructure.Storage.Common.Database;

/// <summary>Runs reads against the database alongside writes, each read on a connection of its own.</summary>
internal sealed class DatabaseReader(Database database)
{
	// ponytail: a fixed number of reads at once, so a burst cannot take every pooled connection; tune if reads queue up.
	private readonly SemaphoreSlim _readSlots = new(Environment.ProcessorCount * 2);

	/// <summary>Runs a query on a connection of its own, alongside other reads; it sees what is committed.</summary>
	/// <returns>The result of the query.</returns>
	public async Task<T> ReadAsync<T>(Func<NpgsqlConnection, Task<T>> query,
		CancellationToken cancellationToken = default)
	{
		await _readSlots.WaitAsync(cancellationToken);
		try
		{
			await using var connection = await database.SynchronousSource.OpenConnectionAsync(cancellationToken);
			return await query(connection);
		}
		finally
		{
			_readSlots.Release();
		}
	}
}