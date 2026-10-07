using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Storage.Batching;

/// <summary>A write waiting for its batch.</summary>
/// <param name="identity">What the operation stores; a later write for the same identity replaces it.</param>
/// <param name="run">The statements of the operation.</param>
internal sealed class DatabaseOperation(object identity, Func<SqliteConnection, SqliteTransaction, Task> run)
{
	public object Identity { get; } = identity;

	/// <summary>Gets or sets the statements of the latest operation queued for the identity.</summary>
	public Func<SqliteConnection, SqliteTransaction, Task> Run { get; set; } = run;

	/// <summary>Gets whether a caller waits for the outcome, so a failure is the caller's to handle.</summary>
	public bool Awaited { get; init; }

	/// <summary>Completes once the operation is committed, or fails with the reason it was not.</summary>
	public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>Operations the worker runs in order in one transaction, each inside its own savepoint.</summary>
/// <param name="operations">The operations, in the order they run.</param>
internal sealed class DatabaseBatch(IReadOnlyList<DatabaseOperation> operations)
{
	private readonly TaskCompletionSource<Exception?> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public IReadOnlyList<DatabaseOperation> Operations { get; } = operations;

	/// <summary>Gets the failure of each operation that was rolled back alone, by position.</summary>
	public Exception?[] Errors { get; } = new Exception?[operations.Count];

	/// <summary>Gets a task that completes once the batch has run: with no exception when it was committed.</summary>
	public Task<Exception?> Completion => _done.Task;

	public void Complete(Exception? failure) => _done.TrySetResult(failure);
}

/// <summary>A read the worker runs at once on a connection of its own.</summary>
/// <param name="run">The query; it reports its own result.</param>
/// <param name="fail">Reports a failure to get a connection for the query.</param>
internal sealed class ReadOperation(Func<SqliteConnection, Task> run, Action<Exception> fail)
{
	public Func<SqliteConnection, Task> Run { get; } = run;

	public Action<Exception> Fail { get; } = fail;
}

/// <summary>The SQLite result codes the storage reacts to.</summary>
internal static class SqliteErrors
{
	/// <summary>Another connection holds the database.</summary>
	public const int Busy = 5;

	/// <summary>A table is locked by another statement of the same connection.</summary>
	public const int Locked = 6;

	/// <summary>A unique, foreign key, check or not-null rule refused the change.</summary>
	public const int Constraint = 19;
}