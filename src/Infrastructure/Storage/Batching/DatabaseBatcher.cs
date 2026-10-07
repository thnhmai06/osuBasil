using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Storage.Batching;

/// <summary>Takes every read and write the repositories make and decides when the database worker runs them.</summary>
/// <remarks>
///     Writes run in the order they were queued, in batches of one transaction: a batch is due once
///     <see cref="MaxCount" /> writes are pending or the oldest has waited <see cref="MaxAge" />. A later write for the
///     same identity replaces the queued one. A write completes only once its batch is committed, or fails with the
///     reason it was not. Reads are not batched: they go to the worker at once and, as in SQLite's WAL mode, see what
///     is committed, not the writes still queued. An operation uses only the connection it is given and never queues
///     another operation: the batch it runs in would wait for itself.
/// </remarks>
internal sealed class DatabaseBatcher(TimeProvider timeProvider, ILogger<DatabaseBatcher> logger)
	: BackgroundService, IHostedLifecycleService
{
	/// <summary>The number of pending writes that makes a batch due.</summary>
	public const int MaxCount = 100;

	/// <summary>How long the oldest pending write may wait before its batch is due.</summary>
	public static readonly TimeSpan MaxAge = TimeSpan.FromMilliseconds(50);

	private readonly Channel<DatabaseBatch> _batches = Channel.CreateUnbounded<DatabaseBatch>(
		new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

	private readonly Channel<ReadOperation> _reads = Channel.CreateUnbounded<ReadOperation>(
		new UnboundedChannelOptions { SingleReader = true });

	private readonly Lock _gate = new();
	private OrderedDictionary<object, DatabaseOperation> _pending = new();
	private TaskCompletionSource _changed = NewSignal();
	private long _oldest;

	/// <summary>Gets the batches of operations, in the order they must run.</summary>
	public ChannelReader<DatabaseBatch> Batches => _batches.Reader;

	/// <summary>Gets the reads, in the order they were made; they run side by side.</summary>
	public ChannelReader<ReadOperation> Reads => _reads.Reader;

	/// <summary>Queues a write of an identity, replacing any queued write of the same identity.</summary>
	/// <returns>A task that completes once the write is committed.</returns>
	public Task EnqueueAsync(object identity, Func<SqliteConnection, SqliteTransaction, Task> write)
	{
		lock (_gate)
		{
			if (_pending.TryGetValue(identity, out var queued))
			{
				queued.Run = write;
				return queued.Done.Task;
			}

			var operation = new DatabaseOperation(identity, write);
			Add(operation);
			return operation.Done.Task;
		}
	}

	/// <summary>Queues a write that no other write replaces, such as a line of history.</summary>
	/// <returns>A task that completes once the write is committed.</returns>
	public Task AppendAsync(Func<SqliteConnection, SqliteTransaction, Task> write) => EnqueueAsync(new object(), write);

	/// <summary>Queues a write that produces a result, such as an insert that assigns an id.</summary>
	/// <returns>The result, once the write is committed.</returns>
	public async Task<T> WriteAsync<T>(Func<SqliteConnection, SqliteTransaction, Task<T>> command,
		CancellationToken cancellationToken = default)
	{
		T result = default!;
		await AppendAsync(async (connection, transaction) => result = await command(connection, transaction))
			.WaitAsync(cancellationToken);
		return result;
	}

	/// <summary>Runs a query at once, alongside other reads; it sees what is committed.</summary>
	/// <returns>The result of the query.</returns>
	public Task<T> ReadAsync<T>(Func<SqliteConnection, Task<T>> query, CancellationToken cancellationToken = default)
	{
		var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
		var read = new ReadOperation(async connection => result.TrySetResult(await query(connection)),
			exception => result.TrySetException(exception));
		if (!_reads.Writer.TryWrite(read))
			read.Fail(new InvalidOperationException("The database is closed."));

		return result.Task.WaitAsync(cancellationToken);
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		try
		{
			while (true)
			{
				await WaitUntilDueAsync(stoppingToken);
				await RunBatchAsync();
			}
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			// Shutdown stops the loop; StoppedAsync hands over what is still queued.
		}
	}

	public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	/// <summary>Hands over every operation still queued once the rest of the host has stopped, then closes.</summary>
	public async Task StoppedAsync(CancellationToken cancellationToken)
	{
		while (HasPending())
			await RunBatchAsync();

		_batches.Writer.Complete();
		_reads.Writer.Complete();
	}

	private void Add(DatabaseOperation operation)
	{
		_pending.Add(operation.Identity, operation);
		if (_pending.Count == 1)
			_oldest = timeProvider.GetTimestamp();
		if (_pending.Count is 1 or MaxCount)
			_changed.TrySetResult();
	}

	/// <summary>Completes when a batch is due.</summary>
	private async Task WaitUntilDueAsync(CancellationToken cancellationToken)
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

	/// <summary>Hands the pending operations to the worker as one batch and settles them once it has run.</summary>
	private async Task RunBatchAsync()
	{
		DatabaseBatch batch;
		lock (_gate)
		{
			if (_pending.Count == 0)
				return;

			batch = new DatabaseBatch([.. _pending.Values]);
			_pending = new OrderedDictionary<object, DatabaseOperation>();
		}

		await _batches.Writer.WriteAsync(batch);
		var failure = await batch.Completion;

		// A failed operation is not tried again: a later attempt could overwrite a newer write. Its caller gets the error.
		for (var index = 0; index < batch.Operations.Count; index++)
		{
			var operation = batch.Operations[index];
			if ((failure ?? batch.Errors[index]) is { } error)
			{
				logger.LogWarning(error, "Database operation for {Identity} failed", operation.Identity);
				operation.Done.TrySetException(error);
			}
			else
			{
				operation.Done.TrySetResult();
			}
		}
	}

	private bool HasPending()
	{
		lock (_gate)
			return _pending.Count > 0;
	}

	private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}