using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Storage.Batching;

/// <summary>Takes every read and write the repositories make and decides when the database worker runs them.</summary>
/// <remarks>
///     Operations run in the order they were queued, in batches of one transaction: a batch is due once
///     <see cref="MaxCount" /> writes are pending or the oldest has waited <see cref="MaxAge" />. A later write for the
///     same identity replaces the queued one. A read therefore sees every write queued before it. Every operation
///     completes only once its batch is committed, or fails with the reason it was not. An operation uses only the
///     connection and transaction it is given and never queues another operation: the batch it runs in would wait for
///     itself.
/// </remarks>
internal sealed class DatabaseBatcher(TimeProvider timeProvider, ILogger<DatabaseBatcher> logger)
	: BackgroundService, IHostedLifecycleService
{
	/// <summary>The number of pending writes that makes a batch due; reads do not count.</summary>
	public const int MaxCount = 100;

	/// <summary>How long the oldest pending operation may wait before its batch is due.</summary>
	public static readonly TimeSpan MaxAge = TimeSpan.FromMilliseconds(50);

	private readonly Channel<DatabaseBatch> _batches = Channel.CreateUnbounded<DatabaseBatch>(
		new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

	private readonly Lock _gate = new();
	private OrderedDictionary<object, DatabaseOperation> _pending = new();
	private TaskCompletionSource _changed = NewSignal();
	private long _oldest;
	private int _writes;

	/// <summary>Gets the batches of operations, in the order they must run.</summary>
	public ChannelReader<DatabaseBatch> Batches => _batches.Reader;

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
	public Task<T> WriteAsync<T>(Func<SqliteConnection, SqliteTransaction, Task<T>> command,
		CancellationToken cancellationToken = default) => RunAsync(command, false, cancellationToken);

	/// <summary>Queues a query; it sees every write queued before it.</summary>
	/// <returns>The result of the query.</returns>
	public Task<T> ReadAsync<T>(Func<SqliteConnection, SqliteTransaction, Task<T>> query,
		CancellationToken cancellationToken = default) => RunAsync(query, true, cancellationToken);

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
	}

	private async Task<T> RunAsync<T>(Func<SqliteConnection, SqliteTransaction, Task<T>> operation, bool isRead,
		CancellationToken cancellationToken)
	{
		T result = default!;
		var queued = new DatabaseOperation(new object(),
			async (connection, transaction) => result = await operation(connection, transaction), isRead);
		lock (_gate)
			Add(queued);

		await queued.Done.Task.WaitAsync(cancellationToken);
		return result;
	}

	private void Add(DatabaseOperation operation)
	{
		_pending.Add(operation.Identity, operation);
		if (!operation.IsRead)
			_writes++;
		if (_pending.Count == 1)
			_oldest = timeProvider.GetTimestamp();
		if (_pending.Count == 1 || _writes == MaxCount)
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
				if (_writes >= MaxCount)
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
			_writes = 0;
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
