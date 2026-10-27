using System.Collections.Concurrent;
using Basil.Infrastructure.Persistence.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Persistence.Postgres;

/// <summary>
///     Stores every change as its own statement, keeps the writes of one <see cref="Root" /> in order, and retries
///     only errors caused by the environment.
/// </summary>
internal sealed class DatabaseWriter(
	Database database,
	IOptions<DatabaseOptions> options,
	TimeProvider timeProvider,
	ILogger<DatabaseWriter> logger) : IHostedLifecycleService, IDisposable
{
	private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromMilliseconds(50);
	private static readonly TimeSpan LongestRetryDelay = TimeSpan.FromSeconds(5);

	private readonly CancellationTokenSource _abandon = new();
	private readonly SemaphoreSlim _connections = new(ValidConnections(options.Value.WriteConnections));
	private readonly ConcurrentDictionary<RootQueue, Task> _drains = new();
	private readonly ConcurrentDictionary<Root, RootQueue> _queues = new();
	private readonly CancellationTokenSource _stopping = new();
	private int _abandonedCount;
	private Task? _ticketCleanup;

	/// <inheritdoc />
	public void Dispose()
	{
		_abandon.Dispose();
		_stopping.Dispose();
		_connections.Dispose();
	}

	/// <inheritdoc />
	public Task StartingAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task StartAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task StartedAsync(CancellationToken cancellationToken)
	{
		_ticketCleanup = ForgetTicketsEveryHourAsync();
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task StoppingAsync(CancellationToken cancellationToken)
	{
		await _stopping.CancelAsync();
		if (_ticketCleanup is not null)
			await _ticketCleanup;
	}

	/// <inheritdoc />
	public Task StopAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}

	/// <summary>
	///     Waits until every queued write is stored; when the shutdown times out, the writes not stored yet fail and
	///     their number is logged.
	/// </summary>
	public async Task StoppedAsync(CancellationToken cancellationToken)
	{
		await using var registration = cancellationToken.Register(_abandon.Cancel);
		while (_drains.Values.Where(task => !task.IsCompleted).ToArray() is { Length: > 0 } running)
			await Task.WhenAll(running);
		var count = Volatile.Read(ref _abandonedCount);
		if (count > 0)
			logger.LogCritical("{Count} writes were not stored before the server stopped", count);
	}

	/// <summary>Queues the state of an identity, replacing a queued state of the same identity that has not been sent yet.</summary>
	/// <returns>
	///     A task that completes once the state is committed and fails when the database refuses it; callers that only
	///     record a change need not wait.
	/// </returns>
	public Task SaveAsync(Root root, object identity, IDatabaseCommand command)
	{
		var pending = new PendingTransaction(identity, command, false, false);
		return Enqueue(root, identity, pending).Pending.Done.Task;
	}

	/// <summary>
	///     Queues a write that no other write replaces, such as a line of history. It runs in a transaction of its own
	///     whose commit is confirmed, so it is stored exactly once even when the connection drops while committing.
	/// </summary>
	/// <returns>
	///     A task that completes once the write is committed and fails when the database refuses it; a refusal is logged,
	///     so callers that only record a change need not wait.
	/// </returns>
	public Task AppendAsync(Root root, IDatabaseCommand command)
	{
		var identity = new object();
		var pending = new PendingTransaction(identity, command, true, true);
		return Enqueue(root, identity, pending).Pending.Done.Task;
	}

	/// <summary>
	///     Runs a write whose outcome the caller needs, after the earlier writes of its root, in a transaction of its
	///     own.
	/// </summary>
	/// <remarks>The token cancels it only while it waits for its turn; once it runs, it runs to its result.</remarks>
	public async Task<T> WriteAsync<TContext, T>(Root root,
		Func<TContext, CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
		where TContext : DbContext, IDatabaseContext<TContext>, IWriteTicketContext
	{
		T result = default!;
		var identity = new object();
		var command = new WriteCommand<TContext>(async (context, token) => result = await operation(context, token));
		var pending = new PendingTransaction(identity, command, true, false);
		var queue = Enqueue(root, identity, pending).Queue;
		await using var registration = cancellationToken.Register(() =>
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

	/// <summary>
	///     Runs a write that gives no value, after the earlier writes of its root, in a transaction of its own.
	/// </summary>
	/// <remarks>The token cancels it only while it waits for its turn; once it runs, it runs to its result.</remarks>
	public Task WriteAsync<TContext>(Root root, Func<TContext, CancellationToken, Task> operation,
		CancellationToken cancellationToken = default)
		where TContext : DbContext, IDatabaseContext<TContext>, IWriteTicketContext
	{
		return WriteAsync<TContext, bool>(root, async (context, token) =>
		{
			await operation(context, token);
			return true;
		}, cancellationToken);
	}

	/// <summary>
	///     Stops the roots from storing anything until the returned hold is disposed, once every write queued before
	///     the call has been stored.
	/// </summary>
	/// <param name="roots">The roots to stop; a write that changes several roots holds all of them.</param>
	/// <param name="cancellationToken">A token that cancels the wait for the roots to be reached.</param>
	/// <returns>The hold; disposing it lets the roots store their later writes.</returns>
	/// <remarks>The roots are taken in a fixed order, so two holds over overlapping roots cannot wait for each other.</remarks>
	public async Task<IAsyncDisposable> HoldAsync(IEnumerable<Root> roots,
		CancellationToken cancellationToken = default)
	{
		var hold = new RootHold();
		try
		{
			foreach (var root in roots.Distinct().OrderBy(root => root.Kind).ThenBy(root => root.Id))
			{
				var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
				var identity = new object();
				var pending = new PendingTransaction(identity, null, false, false, async () =>
				{
					reached.TrySetResult();
					await hold.Released.WaitAsync(_abandon.Token);
				});
				Enqueue(root, identity, pending);
				hold.Add(pending.Done.Task);
				await reached.Task.WaitAsync(cancellationToken);
			}
		}
		catch
		{
			await hold.DisposeAsync();
			throw;
		}

		return hold;
	}

	private static int ValidConnections(int count)
	{
		return count >= 1
			? count
			: throw new ArgumentOutOfRangeException(nameof(count), count, "At least one write connection is required.");
	}

	private (RootQueue Queue, PendingTransaction Pending) Enqueue(Root root, object identity,
		PendingTransaction pendingTransaction)
	{
		while (true)
		{
			var queue = _queues.GetOrAdd(root, _ => new RootQueue());
			lock (queue.Gate)
			{
				if (queue.Retired)
					continue;
				if (pendingTransaction.Command is not null && queue.Pending.TryGetValue(identity, out var queued) &&
				    queued.Command is not null && !queued.IsTryOperation)
				{
					queued.Command = pendingTransaction.Command;
					return (queue, queued);
				}

				queue.Pending.Add(identity, pendingTransaction);
				if (!queue.Draining)
				{
					queue.Draining = true;
					var drain = Task.Run(() => DrainAsync(root, queue));
					_drains[queue] = drain;
					drain.ContinueWith(_ => _drains.TryRemove(new KeyValuePair<RootQueue, Task>(queue, drain)),
						TaskScheduler.Default);
				}

				return (queue, pendingTransaction);
			}
		}
	}

	private async Task DrainAsync(Root root, RootQueue queue)
	{
		while (true)
		{
			PendingTransaction next;
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

	private async Task RunAsync(Root root, PendingTransaction pendingTransaction)
	{
		var delay = FirstRetryDelay;
		DateTimeOffset? firstFailure = null;
		while (true)
			try
			{
				Exception? environmentException = null;
				Exception? failException = null;

				try
				{
					if (pendingTransaction.Hold is { } hold)
					{
						await hold();
						pendingTransaction.Done.TrySetResult();
						return;
					}

					await _connections.WaitAsync(_abandon.Token);
					try
					{
						if (!pendingTransaction.IsTryOperation)
							await RunSnapshotAsync(pendingTransaction);
						else
							await RunTryOpAsync(pendingTransaction);
						pendingTransaction.Done.TrySetResult();
						return;
					}
					finally
					{
						_connections.Release();
					}
				}
				catch (Exception exception) when (!_abandon.IsCancellationRequested &&
				                                  exception.IsEnvironmentException())
				{
					environmentException = exception;
				}
				catch (Exception exception) when (exception is not OperationCanceledException ||
				                                  !_abandon.IsCancellationRequested)
				{
					failException = exception;
				}

				if (environmentException is not null)
				{
					firstFailure ??= timeProvider.GetUtcNow();
					var elapsed = timeProvider.GetUtcNow() - firstFailure.Value;
					if (elapsed < TimeSpan.FromMinutes(1))
						logger.LogWarning(environmentException,
							"A write of {Root} could not be stored; trying again in {Delay}", root, delay);
					else
						logger.LogError(environmentException,
							"A write of {Root} could not be stored; trying again in {Delay}", root, delay);
					await Task.Delay(delay, timeProvider, _abandon.Token);
					delay = NextRetryDelay(delay);
					continue;
				}

				if (failException is not null)
				{
					if (!pendingTransaction.IsTryOperation || pendingTransaction.LogsRefusal)
						logger.LogError(failException, "A write of {Root} for {Identity} was refused and not stored",
							root, pendingTransaction.Identity);
					pendingTransaction.Done.TrySetException(failException);
					return;
				}
			}
			catch (OperationCanceledException) when (_abandon.IsCancellationRequested)
			{
				Interlocked.Increment(ref _abandonedCount);
				pendingTransaction.Done.TrySetException(new OperationCanceledException(_abandon.Token));
				return;
			}
	}

	private async Task RunSnapshotAsync(PendingTransaction pendingTransaction)
	{
		var command = pendingTransaction.Command!;
		await using var context = command.CreateContext(database, confirmed: false);
		await command.RunAsync(context, _abandon.Token);
	}

	private async Task RunTryOpAsync(PendingTransaction pendingTransaction)
	{
		var ticket = Guid.CreateVersion7();
		var committing = false;
		try
		{
			var command = pendingTransaction.Command!;
			await using var context = command.CreateContext(database, confirmed: true);
			await using var transaction = await context.Database.BeginTransactionAsync(_abandon.Token);

			// The ticket commits with the write, so it tells afterwards whether the write was stored.
			command.Tickets(context).Add(new WriteTicketRow { Id = ticket, CreatedAt = timeProvider.GetUtcNow() });
			await context.SaveChangesAsync(_abandon.Token);

			await command.RunAsync(context, _abandon.Token);
			committing = true;
			await transaction.CommitAsync(_abandon.Token);
		}
		catch (Exception exception) when (committing && exception.IsConnectionException())
		{
			if (await WasCommittedAsync(pendingTransaction.Command!, ticket))
				return;
			throw;
		}
	}

	private async Task<bool> WasCommittedAsync(IDatabaseCommand command, Guid ticket)
	{
		var delay = FirstRetryDelay;
		while (true)
			try
			{
				await using var context = command.CreateContext(database, confirmed: true);
				return await command.Tickets(context).AnyAsync(row => row.Id == ticket, _abandon.Token);
			}
			catch (Exception exception) when (exception.IsEnvironmentException())
			{
				logger.LogWarning(exception, "Transaction outcome could not be checked; retrying in {Delay}", delay);
				await Task.Delay(delay, timeProvider, _abandon.Token);
				delay = NextRetryDelay(delay);
			}
	}

	/// <summary>Returns the delay before the next try, after a try that waited the given delay.</summary>
	private static TimeSpan NextRetryDelay(TimeSpan delay)
	{
		return delay + delay > LongestRetryDelay ? LongestRetryDelay : delay + delay;
	}

	private async Task ForgetTicketsEveryHourAsync()
	{
		using var timer = new PeriodicTimer(TimeSpan.FromHours(1), timeProvider);
		try
		{
			do
			{
				try
				{
					await ForgetOldTicketsAsync(_stopping.Token);
				}
				catch (Exception exception) when (exception is not OperationCanceledException)
				{
					logger.LogWarning(exception, "Old write tickets could not be removed; trying again in an hour");
				}
			} while (await timer.WaitForNextTickAsync(_stopping.Token));
		}
		catch (OperationCanceledException)
		{
			// The server is stopping.
		}
	}

	/// <summary>Removes the tickets that are too old to be asked about any more.</summary>
	/// <param name="cancellationToken">A token that cancels the cleanup.</param>
	public async Task ForgetOldTicketsAsync(CancellationToken cancellationToken = default)
	{
		var oldest = timeProvider.GetUtcNow() - TimeSpan.FromDays(1);
		await using var context = database.CreateUnconfirmedContext<CommonDbContext>();
		await context.WriteTickets.Where(row => row.CreatedAt < oldest).ExecuteDeleteAsync(cancellationToken);
	}

	private sealed class RootQueue
	{
		public bool Draining;
		public bool Retired;
		public Lock Gate { get; } = new();
		public OrderedDictionary<object, PendingTransaction> Pending { get; } = new();
	}

	private sealed class RootHold : IAsyncDisposable
	{
		private readonly List<Task> _holds = [];
		private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task Released => _released.Task;

		public async ValueTask DisposeAsync()
		{
			_released.TrySetResult();
			await Task.WhenAll(_holds);
		}

		public void Add(Task done)
		{
			_holds.Add(done);
		}
	}

	private sealed class PendingTransaction(
		object identity,
		IDatabaseCommand? command,
		bool isTryOperation,
		bool logsRefusal,
		Func<Task>? hold = null)
	{
		public bool Started;
		public object Identity { get; } = identity;
		public IDatabaseCommand? Command { get; set; } = command;
		public bool IsTryOperation { get; } = isTryOperation;
		public bool LogsRefusal { get; } = logsRefusal;
		public Func<Task>? Hold { get; } = hold;
		public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
	}
}