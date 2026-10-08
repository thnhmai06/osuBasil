using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Basil.Infrastructure.Storage.Writing;

/// <summary>Stores every change as its own statement, keeps the writes of one <see cref="Root"/> in order, and retries only errors caused by the environment.</summary>
internal sealed class DatabaseWriter(
	Database database,
	IOptions<DatabaseOptions> options,
	TimeProvider timeProvider,
	ILogger<DatabaseWriter> logger) : IHostedLifecycleService, IDisposable
{
	private readonly CancellationTokenSource _abandon = new();
	private readonly SemaphoreSlim _connections = new(ValidConnections(options.Value.WriteConnections));
	private readonly ConcurrentDictionary<Root, RootQueue> _queues = new();
	private readonly ConcurrentDictionary<RootQueue, Task> _drains = new();
	private int _abandonedCount;

	/// <summary>Queues the state of an identity, replacing a queued state of the same identity that has not been sent yet.</summary>
	/// <returns>A task that completes once the state is committed and fails when the database refuses it; callers that only record a change need not wait.</returns>
	public Task SaveAsync(Root root, object identity, WriteCommand command)
	{
		var pending = new Pending(identity, command, null);
		return Enqueue(root, identity, pending).Pending.Done.Task;
	}

	/// <summary>Queues a write that no other write replaces, such as a line of history. The command must be safe to run twice (insert … on conflict (id) do nothing).</summary>
	public Task AppendAsync(Root root, WriteCommand command)
	{
		var identity = new object();
		var pending = new Pending(identity, command, null);
		return Enqueue(root, identity, pending).Pending.Done.Task;
	}

	/// <summary>Runs a write whose outcome the caller needs, after the earlier writes of its root, in a transaction of its own.</summary>
	/// <remarks>The token cancels it only while it waits for its turn; once it runs, it runs to its result.</remarks>
	public async Task<T> WriteAsync<T>(Root root, Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> operation,
		CancellationToken cancellationToken = default)
	{
		T result = default!;
		var identity = new object();
		var pending = new Pending(identity, null, async (connection, transaction) => result = await operation(connection, transaction));
		var queue = Enqueue(root, identity, pending).Queue;
		using var registration = cancellationToken.Register(() =>
		{
			lock (queue.Gate)
			{
				if (!pending.Started && queue.Pending.Remove(pending.Identity))
					pending.Done.TrySetCanceled(cancellationToken);
			}
		});
		await pending.Done.Task;
		return result;
	}

	/// <inheritdoc />
	public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	/// <inheritdoc />
	public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	/// <inheritdoc />
	public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	/// <inheritdoc />
	public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	/// <inheritdoc />
	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	/// <summary>Waits until every queued write is stored; when the shutdown times out, the writes not stored yet fail and their number is logged.</summary>
	public async Task StoppedAsync(CancellationToken cancellationToken)
	{
		await using var registration = cancellationToken.Register(_abandon.Cancel);
		while (_drains.Values.Where(task => !task.IsCompleted).ToArray() is { Length: > 0 } running)
			await Task.WhenAll(running);
		var count = Volatile.Read(ref _abandonedCount);
		if (count > 0)
			logger.LogCritical("{Count} writes were not stored before the server stopped", count);
	}

	/// <inheritdoc />
	public void Dispose()
	{
		_abandon.Dispose();
		_connections.Dispose();
	}

	private static int ValidConnections(int count) => count >= 1
		? count
		: throw new ArgumentOutOfRangeException(nameof(count), count, "At least one write connection is required.");

	private (RootQueue Queue, Pending Pending) Enqueue(Root root, object identity, Pending pending)
	{
		while (true)
		{
			var queue = _queues.GetOrAdd(root, _ => new RootQueue());
			lock (queue.Gate)
			{
				if (queue.Retired)
					continue;
				if (pending.Command is not null && queue.Pending.TryGetValue(identity, out var queued) &&
				    queued.Command is not null && queued.Run is null)
				{
					queued.Command = pending.Command;
					return (queue, queued);
				}
				queue.Pending.Add(identity, pending);
				if (!queue.Draining)
				{
					queue.Draining = true;
					var drain = Task.Run(() => DrainAsync(root, queue));
					_drains[queue] = drain;
					drain.ContinueWith(_ => _drains.TryRemove(new KeyValuePair<RootQueue, Task>(queue, drain)),
						TaskScheduler.Default);
				}
				return (queue, pending);
			}
		}
	}

	private async Task DrainAsync(Root root, RootQueue queue)
	{
		while (true)
		{
			Pending next;
			lock (queue.Gate)
			{
				if (queue.Pending.Count == 0)
				{
					queue.Draining = false;
					queue.Retired = true;
					_queues.TryRemove(new KeyValuePair<Root, RootQueue>(root, queue));
					return;
				}
				next = queue.Pending.GetAt(0).Value;
				queue.Pending.RemoveAt(0);
				next.Started = true;
			}
			await RunAsync(root, next);
		}
	}

	private async Task RunAsync(Root root, Pending pending)
	{
		var delay = TimeSpan.FromMilliseconds(50);
		DateTimeOffset? firstFailure = null;
		while (true)
		{
			try
			{
				Exception? environmentException = null;
				Exception? failException = null;

				try
				{
					await _connections.WaitAsync(_abandon.Token);
					try
					{
						if (pending.Run is null)
							await RunSnapshotAsync(pending);
						else
							await RunTryOpAsync(pending);
						pending.Done.TrySetResult();
						return;
					}
					finally
					{
						_connections.Release();
					}
				}
				catch (Exception exception) when (!_abandon.IsCancellationRequested && WriteErrors.IsEnvironment(exception))
				{
					environmentException = exception;
				}
				catch (Exception exception) when (exception is not OperationCanceledException || !_abandon.IsCancellationRequested)
				{
					failException = exception;
				}

				if (environmentException is not null)
				{
					firstFailure ??= timeProvider.GetUtcNow();
					var elapsed = timeProvider.GetUtcNow() - firstFailure.Value;
					if (elapsed < TimeSpan.FromMinutes(1))
						logger.LogWarning(environmentException, "A write of {Root} could not be stored; trying again in {Delay}", root, delay);
					else
						logger.LogError(environmentException, "A write of {Root} could not be stored; trying again in {Delay}", root, delay);
					await Task.Delay(delay, timeProvider, _abandon.Token);
					delay = delay + delay > TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5) : delay + delay;
					continue;
				}

				if (failException is not null)
				{
					if (pending.Run is not null)
						pending.Done.TrySetException(failException);
					else
					{
						logger.LogError(failException, "A write of {Root} for {Identity} was refused and not stored", root, pending.Identity);
						pending.Done.TrySetException(failException);
					}
					return;
				}
			}
			catch (OperationCanceledException) when (_abandon.IsCancellationRequested)
			{
				Interlocked.Increment(ref _abandonedCount);
				pending.Done.TrySetException(new OperationCanceledException(_abandon.Token));
				return;
			}
		}
	}

	private async Task RunSnapshotAsync(Pending pending)
	{
		await using var connection = await database.OpenSnapshotConnectionAsync(_abandon.Token);
		await using var command = pending.Command!.ToCommand(connection);
		await command.ExecuteNonQueryAsync(_abandon.Token);
	}

	private async Task RunTryOpAsync(Pending pending)
	{
		long xid = 0;
		var committing = false;
		try
		{
			await using var connection = await database.OpenAsync(_abandon.Token);
			await using var transaction = await connection.BeginTransactionAsync(_abandon.Token);
			await using (var timeout = new NpgsqlCommand("set local idle_in_transaction_session_timeout = '30s'", connection, transaction))
				await timeout.ExecuteNonQueryAsync(_abandon.Token);
			await using (var xactId = new NpgsqlCommand("select pg_current_xact_id()::text::bigint", connection, transaction))
				xid = Convert.ToInt64(await xactId.ExecuteScalarAsync(_abandon.Token));
			await pending.Run!(connection, transaction);
			committing = true;
			await transaction.CommitAsync(_abandon.Token);
		}
		catch (NpgsqlException exception) when (committing && exception is not PostgresException)
		{
			if (await WasCommittedAsync(xid))
				return;
			throw;
		}
	}

	private async Task<bool> WasCommittedAsync(long xid)
	{
		var delay = TimeSpan.FromMilliseconds(50);
		while (true)
		{
			try
			{
				await using var connection = await database.OpenAsync(_abandon.Token);
				await using var command = new NpgsqlCommand("select pg_xact_status(@Xid::text::xid8)", connection);
				command.Parameters.AddWithValue("Xid", xid);
				var status = (string?)await command.ExecuteScalarAsync(_abandon.Token);
				switch (status)
				{
					case "committed":
						return true;
					case "in progress":
						await Task.Delay(TimeSpan.FromMilliseconds(50), timeProvider, _abandon.Token);
						break;
					default:
						return false;
				}
			}
			catch (Exception exception) when (WriteErrors.IsEnvironment(exception))
			{
				logger.LogWarning(exception, "Transaction status could not be checked; retrying in {Delay}", delay);
				await Task.Delay(delay, timeProvider, _abandon.Token);
				delay = delay + delay > TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5) : delay + delay;
			}
			catch (PostgresException)
			{
				return false;
			}
		}
	}

	private sealed class RootQueue
	{
		public Lock Gate { get; } = new();
		public OrderedDictionary<object, Pending> Pending { get; } = new();
		public bool Draining;
		public bool Retired;
	}

	private sealed class Pending(object identity, WriteCommand? command,
		Func<NpgsqlConnection, NpgsqlTransaction, Task>? run)
	{
		public object Identity { get; } = identity;
		public WriteCommand? Command { get; set; } = command;
		public Func<NpgsqlConnection, NpgsqlTransaction, Task>? Run { get; } = run;
		public bool Started;
		public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
	}
}
