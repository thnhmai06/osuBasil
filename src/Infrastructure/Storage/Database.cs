using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Basil.Infrastructure.Storage;

/// <summary>Gives access to the server's database: connections for writes, and reads of what is committed.</summary>
internal sealed class Database(IOptions<DatabaseOptions> options) : IAsyncDisposable
{
	private readonly NpgsqlDataSource _source = new NpgsqlDataSourceBuilder(options.Value.ConnectionString)
	{
		// The writer needs to know which statement of a batch was refused.
		ConnectionStringBuilder = { IncludeFailedBatchedCommand = true }
	}.Build();

	// ponytail: a fixed number of reads at once, so a burst cannot take every pooled connection; tune if reads queue up.
	private readonly SemaphoreSlim _readSlots = new(Environment.ProcessorCount * 2);

	static Database() => DefaultTypeMap.MatchNamesWithUnderscores = true;

	/// <summary>Opens a connection to the database.</summary>
	public ValueTask<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken = default) =>
		_source.OpenConnectionAsync(cancellationToken);

	/// <summary>Runs a query on a connection of its own, alongside other reads; it sees what is committed.</summary>
	/// <returns>The result of the query.</returns>
	public async Task<T> ReadAsync<T>(Func<NpgsqlConnection, Task<T>> query, CancellationToken cancellationToken = default)
	{
		await _readSlots.WaitAsync(cancellationToken);
		try
		{
			await using var connection = await _source.OpenConnectionAsync(cancellationToken);
			return await query(connection);
		}
		finally
		{
			_readSlots.Release();
		}
	}

	public ValueTask DisposeAsync() => _source.DisposeAsync();
}
