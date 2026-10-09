using Basil.Infrastructure.Storage.Common.Database;
using Dapper;

namespace Basil.Infrastructure.Storage.Common.Writing;

/// <summary>Hands out ids for rows the server numbers itself, continuing after the highest id stored in a table.</summary>
internal sealed class IdAllocator(DatabaseReader reader, string table) // TODO: Remove this with X-XData pattern
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
					_last = await reader.ReadAsync(connection =>
							connection.ExecuteScalarAsync<long>($"select coalesce(max(id), 0) from {table}"),
						cancellationToken);
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