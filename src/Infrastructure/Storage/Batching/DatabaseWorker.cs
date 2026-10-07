using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;

namespace Basil.Infrastructure.Storage.Batching;

/// <summary>Runs the batches the batcher hands over; the one place that uses the database.</summary>
/// <remarks>
///     Batches run one at a time, each in one transaction with every operation in its own savepoint, so an operation that
///     fails is rolled back alone. The worker keeps running until the batcher has handed over its last batch.
/// </remarks>
internal sealed class DatabaseWorker(Database database, DatabaseBatcher batcher) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		await foreach (var batch in batcher.Batches.ReadAllAsync())
			batch.Complete(await RunAsync(batch));
	}

	/// <summary>Leaves the worker running: it stops once the batcher closes, after its last batch.</summary>
	public override Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	/// <summary>Runs a batch in one transaction.</summary>
	/// <returns><see langword="null" /> when the batch was committed; otherwise why it was not.</returns>
	private async Task<Exception?> RunAsync(DatabaseBatch batch)
	{
		try
		{
			await using var connection = await database.OpenAsync();
			await using var transaction = connection.BeginTransaction(deferred: false);
			for (var index = 0; index < batch.Operations.Count; index++)
			{
				await RunSqlAsync(connection, transaction, "SAVEPOINT operation");
				try
				{
					await batch.Operations[index].Run(connection, transaction);
					await RunSqlAsync(connection, transaction, "RELEASE operation");
				}
				catch (Exception exception) when (exception is not SqliteException
				                                  {
					                                  SqliteErrorCode: SqliteErrors.Busy or SqliteErrors.Locked
				                                  })
				{
					batch.Errors[index] = exception;
					await RunSqlAsync(connection, transaction, "ROLLBACK TO operation; RELEASE operation");
				}
			}

			await transaction.CommitAsync();
			return null;
		}
		catch (Exception exception)
		{
			return exception;
		}
	}

	private static async Task RunSqlAsync(SqliteConnection connection, SqliteTransaction transaction, string sql)
	{
		await using var command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText = sql;
		await command.ExecuteNonQueryAsync();
	}
}
