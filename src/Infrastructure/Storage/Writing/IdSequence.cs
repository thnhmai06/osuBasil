using Dapper;

namespace Basil.Infrastructure.Storage.Writing;

/// <summary>Hands out ids for rows the server numbers itself, continuing after the highest id stored in a table.</summary>
internal sealed class IdSequence(Database database, string table)
{
	private readonly SemaphoreSlim _loading = new(1, 1);
	private long _last;
	private bool _loaded;

	/// <summary>Gets the next id.</summary>
	public async ValueTask<long> NextAsync(CancellationToken cancellationToken = default)
	{
		if (!Volatile.Read(ref _loaded))
		{
			await _loading.WaitAsync(cancellationToken);
			try
			{
				if (!_loaded)
				{
					// `table` is a constant passed by the repository, never user input.
					_last = await database.ReadAsync(connection =>
						connection.ExecuteScalarAsync<long>($"select coalesce(max(id), 0) from {table}"));
					Volatile.Write(ref _loaded, true);
				}
			}
			finally
			{
				_loading.Release();
			}
		}

		return Interlocked.Increment(ref _last);
	}
}