using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Storage.Caching;

/// <summary>Collects pending database writes and commits them together.</summary>
/// <remarks>
///     A batch is due once <see cref="MaxCount" /> writes are pending or the oldest has waited <see cref="MaxAge" />.
///     A read waits only for the writes queued before it.
/// </remarks>
internal sealed class WriteBuffer(Database database, TimeProvider timeProvider, ILogger<WriteBuffer> logger)
{
	/// <summary>The number of pending writes that makes a batch due.</summary>
	public const int MaxCount = 100;

	/// <summary>How long the oldest pending write may wait before its batch is due.</summary>
	public static readonly TimeSpan MaxAge = TimeSpan.FromMilliseconds(50);

	private readonly Lock _gate = new();
	private readonly SemaphoreSlim _flush = new(1, 1);
	private readonly PriorityQueue<TaskCompletionSource, long> _readers = new();
	private OrderedDictionary<object, Pending> _pending = new();
	private TaskCompletionSource _changed = NewSignal();
	private long _oldest;
	private long _last;
	private long _inFlight = long.MaxValue;

	public void Enqueue(object identity, Func<SqliteConnection, SqliteTransaction, Task> write)
	{
		lock (_gate)
		{
			var sequence = _pending.TryGetValue(identity, out var earlier) ? earlier.Sequence : ++_last;
			_pending[identity] = new Pending(sequence, write);
			if (_pending.Count == 1)
				_oldest = timeProvider.GetTimestamp();
			if (_pending.Count is 1 or MaxCount)
				_changed.TrySetResult();
		}
	}

	public void Append(Func<SqliteConnection, SqliteTransaction, Task> write)
	{
		Enqueue(new object(), write);
	}

	/// <summary>Completes once every write queued before the call is in the database.</summary>
	public Task WaitForWritesAsync(CancellationToken cancellationToken = default)
	{
		lock (_gate)
		{
			var target = _last;
			if (Committed() >= target)
				return Task.CompletedTask;

			var reader = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			_readers.Enqueue(reader, target);
			return reader.Task.WaitAsync(cancellationToken);
		}
	}

	/// <summary>Completes when a batch is due.</summary>
	public async Task WaitUntilDueAsync(CancellationToken cancellationToken)
	{
		while (true)
		{
			Task changed;
			TimeSpan? wait = null;
			lock (_gate)
			{
				if (_pending.Count >= MaxCount)
					return;
				if (_pending.Count > 0)
				{
					var age = timeProvider.GetElapsedTime(_oldest);
					if (age >= MaxAge)
						return;
					wait = MaxAge - age;
				}

				if (_changed.Task.IsCompleted)
					_changed = NewSignal();
				changed = _changed.Task;
			}

			if (wait is { } delay)
				await Task.WhenAny(changed, Task.Delay(delay, timeProvider, cancellationToken));
			else
				await changed.WaitAsync(cancellationToken);

			cancellationToken.ThrowIfCancellationRequested();
		}
	}

	/// <summary>Commits every pending write in one transaction.</summary>
	public async Task FlushAsync(CancellationToken cancellationToken = default)
	{
		await _flush.WaitAsync(cancellationToken);
		try
		{
			OrderedDictionary<object, Pending> batch;
			lock (_gate)
			{
				if (_pending.Count == 0)
					return;

				batch = _pending;
				_pending = new OrderedDictionary<object, Pending>();
				_inFlight = batch.Values.Min(pending => pending.Sequence);
			}

			if (!await TryCommitAsync(batch, cancellationToken))
				foreach (var (identity, pending) in batch)
					await RetryAsync(identity, pending, cancellationToken);

			lock (_gate)
			{
				_inFlight = long.MaxValue;
				var committed = Committed();
				while (_readers.TryPeek(out var reader, out var target) && target <= committed)
				{
					_readers.Dequeue();
					reader.TrySetResult();
				}
			}
		}
		finally
		{
			_flush.Release();
		}
	}

	/// <summary>The highest sequence below which every write is in the database.</summary>
	private long Committed()
	{
		var lowest = _inFlight;
		foreach (var pending in _pending.Values)
			lowest = Math.Min(lowest, pending.Sequence);

		return lowest == long.MaxValue ? _last : lowest - 1;
	}

	private async Task<bool> TryCommitAsync(OrderedDictionary<object, Pending> batch,
		CancellationToken cancellationToken)
	{
		try
		{
			await using var connection = await database.OpenAsync(cancellationToken);
			await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
			try
			{
				foreach (var pending in batch.Values)
					await pending.Write(connection, transaction);

				await transaction.CommitAsync(cancellationToken);
				return true;
			}
			catch (Exception exception)
			{
				try
				{
					await transaction.RollbackAsync(CancellationToken.None);
				}
				catch (Exception rollbackException)
				{
					logger.LogWarning(rollbackException, "Failed to roll back a storage write batch");
				}

				logger.LogWarning(exception, "A storage write batch failed; retrying its writes individually");
			}
		}
		catch (Exception exception)
		{
			logger.LogWarning(exception, "Could not start a storage write batch; retrying its writes individually");
		}

		return false;
	}

	private async Task RetryAsync(object identity, Pending pending, CancellationToken cancellationToken)
	{
		lock (_gate)
		{
			// A newer write for the identity replaces this one; it takes the earlier sequence so readers wait for it.
			if (_pending.TryGetValue(identity, out var newer))
			{
				_pending[identity] = newer with { Sequence = Math.Min(newer.Sequence, pending.Sequence) };
				return;
			}
		}

		try
		{
			await using var connection = await database.OpenAsync(cancellationToken);
			await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
			await pending.Write(connection, transaction);
			await transaction.CommitAsync(cancellationToken);
		}
		catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
		{
			logger.LogError(exception, "Dropping a storage write that violates a database constraint for {Identity}",
				identity);
		}
		catch (Exception exception)
		{
			lock (_gate)
			{
				_pending[identity] = _pending.TryGetValue(identity, out var newer)
					? newer with { Sequence = Math.Min(newer.Sequence, pending.Sequence) }
					: pending;
				if (_pending.Count == 1)
					_oldest = timeProvider.GetTimestamp();
			}

			logger.LogError(exception, "Could not persist a storage write for {Identity}; it remains pending",
				identity);
		}
	}

	private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

	/// <summary>A queued write and the sequence of the first write queued for its identity.</summary>
	private readonly record struct Pending(long Sequence, Func<SqliteConnection, SqliteTransaction, Task> Write);
}