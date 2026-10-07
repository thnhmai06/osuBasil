using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Storage.Caching;

/// <summary>Collects pending database writes and commits them together.</summary>
internal sealed class WriteBuffer(Database database, ILogger<WriteBuffer> logger)
{
	private readonly Lock _gate = new();
	private readonly SemaphoreSlim _flush = new(1, 1);
	private OrderedDictionary<object, Func<SqliteConnection, SqliteTransaction, Task>> _pending = new();

	public void Enqueue(object identity, Func<SqliteConnection, SqliteTransaction, Task> write)
	{
		lock (_gate)
			_pending[identity] = write;
	}

	public void Append(Func<SqliteConnection, SqliteTransaction, Task> write)
	{
		Enqueue(new object(), write);
	}

	public async Task FlushAsync(CancellationToken cancellationToken = default)
	{
		await _flush.WaitAsync(cancellationToken);
		try
		{
			OrderedDictionary<object, Func<SqliteConnection, SqliteTransaction, Task>> batch;
			lock (_gate)
			{
				if (_pending.Count == 0)
					return;

				batch = _pending;
				_pending = new OrderedDictionary<object, Func<SqliteConnection, SqliteTransaction, Task>>();
			}

			try
			{
				await using var connection = await database.OpenAsync(cancellationToken);
				await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
				try
				{
					foreach (var (identity, write) in batch)
						await write(connection, transaction);

					await transaction.CommitAsync(cancellationToken);
					return;
				}
				catch (Exception exception)
				{
					try
					{
						await transaction.RollbackAsync(CancellationToken.None);
					}
					catch (Exception rollbackException)
					{
						logger.LogWarning(rollbackException, "Failed to roll back a storage write batch.");
					}

					logger.LogWarning(exception, "A storage write batch failed; retrying its writes individually.");
				}
			}
			catch (Exception exception)
			{
				logger.LogWarning(exception, "Could not start a storage write batch; retrying its writes individually.");
			}

			foreach (var (identity, write) in batch)
				await RetryAsync(identity, write, cancellationToken);
		}
		finally
		{
			_flush.Release();
		}
	}

	private async Task RetryAsync(object identity, Func<SqliteConnection, SqliteTransaction, Task> write,
		CancellationToken cancellationToken)
	{
		if (HasNewer(identity))
			return;

		try
		{
			await using var connection = await database.OpenAsync(cancellationToken);
			await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
			await write(connection, transaction);
			await transaction.CommitAsync(cancellationToken);
		}
		catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
		{
			logger.LogError(exception, "Dropping a storage write that violates a database constraint for {Identity}.", identity);
		}
		catch (Exception exception)
		{
			lock (_gate)
			{
				if (!_pending.ContainsKey(identity))
					_pending.Add(identity, write);
			}

			logger.LogError(exception, "Could not persist a storage write for {Identity}; it remains pending.", identity);
		}
	}

	private bool HasNewer(object identity)
	{
		lock (_gate)
			return _pending.ContainsKey(identity);
	}
}
