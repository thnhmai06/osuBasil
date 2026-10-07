using Microsoft.Extensions.Logging;
using Npgsql;

namespace Basil.Infrastructure.Storage.Writing;

/// <summary>A queue that stores its changes in order in its own transactions.</summary>
/// <remarks>
///     A batch is due once <see cref="MaxCount" /> writes are queued, the oldest has waited <see cref="MaxAge" />, or a
///     try-operation is queued. A batch that fails for a reason outside the server is stored again, before any later
///     write of the lane; a statement the database refuses is dropped and logged; a try-operation learns its outcome
///     only once its batch is committed.
/// </remarks>
internal sealed class WriteLane(Database database, TimeProvider timeProvider, ILogger logger)
{
	/// <summary>The number of pending writes that makes a batch due.</summary>
	public const int MaxCount = 100;

	/// <summary>How long the oldest pending write may wait before its batch is due.</summary>
	public static readonly TimeSpan MaxAge = TimeSpan.FromMilliseconds(50);

	private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromMilliseconds(50);
	private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(5);

	private readonly Lock _gate = new();
	private OrderedDictionary<object, PendingWrite> _pending = new();
	private TaskCompletionSource _changed = NewSignal();
	private long _oldest;
	private bool _urgent;

	/// <summary>Queues a write of an identity, replacing any queued write of the same identity.</summary>
	/// <returns>A task that completes once the write is committed.</returns>
	internal Task Enqueue(object identity, WriteCommand command)
	{
		lock (_gate)
		{
			if (_pending.TryGetValue(identity, out var queued))
			{
				queued.Command = command;
				return queued.Done.Task;
			}

			var pending = new PendingWrite(identity, command, null);
			Add(pending);
			return pending.Done.Task;
		}
	}

	/// <summary>Queues a try-operation, which makes the batch due at once.</summary>
	/// <returns>A task that completes once the batch is committed, or fails with the operation's error.</returns>
	internal Task Enqueue(Func<NpgsqlConnection, NpgsqlTransaction, Task> run)
	{
		lock (_gate)
		{
			var pending = new PendingWrite(new object(), null, run);
			Add(pending);
			_urgent = true;
			_changed.TrySetResult();
			return pending.Done.Task;
		}
	}

	internal async Task WaitUntilDueAsync(CancellationToken cancellationToken)
	{
		while (true)
		{
			Task changed;
			TimeSpan? wait = null;
			lock (_gate)
			{
				if (_pending.Count >= MaxCount || _urgent && _pending.Count > 0)
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

	internal bool HasPending
	{
		get
		{
			lock (_gate)
				return _pending.Count > 0;
		}
	}

	private List<PendingWrite> Take()
	{
		lock (_gate)
		{
			var batch = _pending.Values.ToList();
			_pending = new OrderedDictionary<object, PendingWrite>();
			_urgent = false;
			return batch;
		}
	}

	private async Task StoreAsync(List<PendingWrite> batch, CancellationToken abandon)
	{
		var delay = FirstRetryDelay;
		try
		{
			while (batch.Count > 0)
			{
				switch (await RunOnceAsync(batch))
				{
					case Committed:
						SettleCommitted(batch);
						return;
					case Transient transient:
						logger.LogWarning(transient.Error, "Write batch will be stored again in {Delay}", delay);
						await Task.Delay(delay, timeProvider, abandon);
						delay = Min(delay + delay, MaxRetryDelay);
						break;
					case CommitUnknown unknown:
						logger.LogWarning(unknown.Error, "A write batch lost its connection while committing");
						if (await WasCommittedAsync(unknown.Xid, abandon))
						{
							SettleCommitted(batch);
							return;
						}

						break;
					case Rejected rejected:
						var refused = batch[rejected.Index];
						logger.LogCritical(rejected.Error, "Snapshot write for {Identity} was not stored",
							refused.Identity);
						refused.Done.TrySetException(rejected.Error);
						batch.RemoveAt(rejected.Index);
						break;
					case Failed failed:
						Fail(batch, failed.Error);
						return;
				}
			}
		}
		catch (OperationCanceledException exception) when (abandon.IsCancellationRequested)
		{
			foreach (var pending in batch)
				pending.Done.TrySetException(exception);
			logger.LogCritical(exception, "{Count} writes were not stored", batch.Count);
		}
		catch (Exception exception)
		{
			Fail(batch, exception);
		}
	}

	private async Task<Outcome> RunOnceAsync(List<PendingWrite> batch)
	{
		foreach (var pending in batch)
			pending.Error = null;

		var xid = 0L;
		var committing = false;
		var batchCommands = new Dictionary<NpgsqlBatchCommand, int>(ReferenceEqualityComparer.Instance);
		try
		{
			await using var connection = await database.OpenAsync();
			await using var transaction = await connection.BeginTransactionAsync();
			var hasTryOperation = batch.Any(pending => pending.Run is not null);
			var preludeSql = hasTryOperation
				? "select pg_current_xact_id()::text::bigint"
				: "set local synchronous_commit = off; select pg_current_xact_id()::text::bigint";
			await using (var prelude = new NpgsqlCommand(preludeSql, connection, transaction))
				xid = Convert.ToInt64(await prelude.ExecuteScalarAsync());

			for (var index = 0; index < batch.Count;)
			{
				if (batch[index].Command is not null)
				{
					await using var writeBatch = new NpgsqlBatch(connection, transaction);
					while (index < batch.Count && batch[index].Command is { } command)
					{
						var batchCommand = command.ToBatchCommand();
						batchCommands.Add(batchCommand, index);
						writeBatch.BatchCommands.Add(batchCommand);
						index++;
					}

					await writeBatch.ExecuteNonQueryAsync();
					continue;
				}

				var operation = batch[index++];
				await ExecuteSqlAsync(connection, transaction, "savepoint write");
				try
				{
					await operation.Run!(connection, transaction);
					await ExecuteSqlAsync(connection, transaction, "release savepoint write");
				}
				catch (Exception exception) when (exception is not NpgsqlException { IsTransient: true })
				{
					await ExecuteSqlAsync(connection, transaction, "rollback to savepoint write");
					operation.Error = exception;
				}
			}

			committing = true;
			await transaction.CommitAsync();
			return new Committed();
		}
		catch (NpgsqlException exception) when (committing && exception is not PostgresException)
		{
			return new CommitUnknown(xid, exception);
		}
		catch (NpgsqlException exception) when (exception.IsTransient)
		{
			return new Transient(exception);
		}
		catch (NpgsqlException exception) when (exception.BatchCommand is { } refused &&
		                                        batchCommands.TryGetValue(refused, out var index))
		{
			return new Rejected(index, exception);
		}
		catch (Exception exception)
		{
			return new Failed(exception);
		}
	}

	private async Task<bool> WasCommittedAsync(long xid, CancellationToken abandon)
	{
		var delay = FirstRetryDelay;
		while (true)
		{
			try
			{
				await using var connection = await database.OpenAsync(abandon);
				await using var command = new NpgsqlCommand("select pg_xact_status(@Xid::text::xid8)", connection);
				command.Parameters.AddWithValue("Xid", xid);
				var status = (string?)await command.ExecuteScalarAsync(abandon);
				switch (status)
				{
					case "committed":
						return true;
					case "in progress":
						await Task.Delay(FirstRetryDelay, timeProvider, abandon);
						break;
					default:
						return false;
				}
			}
			catch (NpgsqlException exception) when (exception.IsTransient)
			{
				logger.LogWarning(exception, "Transaction status could not be checked; retrying in {Delay}", delay);
				await Task.Delay(delay, timeProvider, abandon);
				delay = Min(delay + delay, MaxRetryDelay);
			}
			catch (PostgresException)
			{
				// The server no longer knows the transaction (it recovered from a crash), so it was not committed.
				return false;
			}
		}
	}

	/// <summary>Stores every write queued now as one batch.</summary>
	/// <param name="abandon">Stops retrying; the writes not stored by then fail and are logged.</param>
	internal Task StoreDueAsync(CancellationToken abandon) => StoreAsync(Take(), abandon);

	/// <summary>Stores batches until nothing is queued.</summary>
	/// <param name="abandon">Stops retrying; the writes not stored by then fail and are logged.</param>
	internal async Task StorePendingAsync(CancellationToken abandon)
	{
		while (HasPending)
			await StoreDueAsync(abandon);
	}

	private void SettleCommitted(IEnumerable<PendingWrite> batch)
	{
		foreach (var pending in batch)
		{
			if (pending.Error is { } error)
			{
				logger.LogDebug(error, "Try-operation for {Identity} failed", pending.Identity);
				pending.Done.TrySetException(error);
			}
			else
			{
				pending.Done.TrySetResult();
			}
		}
	}

	private void Fail(IEnumerable<PendingWrite> batch, Exception exception)
	{
		foreach (var pending in batch)
		{
			if (pending.Command is not null)
				logger.LogCritical(exception, "Snapshot write for {Identity} was not stored", pending.Identity);
			pending.Done.TrySetException(exception);
		}
	}

	private void Add(PendingWrite pending)
	{
		_pending.Add(pending.Identity, pending);
		if (_pending.Count == 1)
			_oldest = timeProvider.GetTimestamp();
		if (_pending.Count is 1 or MaxCount)
			_changed.TrySetResult();
	}

	private static async Task ExecuteSqlAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
	{
		await using var command = new NpgsqlCommand(sql, connection, transaction);
		await command.ExecuteNonQueryAsync();
	}

	private static TimeSpan Min(TimeSpan first, TimeSpan second) => first < second ? first : second;

	private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

	private sealed class PendingWrite(
		object identity,
		WriteCommand? command,
		Func<NpgsqlConnection, NpgsqlTransaction, Task>? run)
	{
		public object Identity { get; } = identity;
		public WriteCommand? Command { get; set; } = command;
		public Func<NpgsqlConnection, NpgsqlTransaction, Task>? Run { get; } = run;
		public Exception? Error { get; set; }
		public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
	}

	private abstract record Outcome;

	private sealed record Committed : Outcome;

	private sealed record Transient(Exception Error) : Outcome;

	private sealed record CommitUnknown(long Xid, Exception Error) : Outcome;

	private sealed record Rejected(int Index, Exception Error) : Outcome;

	private sealed record Failed(Exception Error) : Outcome;
}