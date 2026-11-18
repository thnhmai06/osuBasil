using Basil.Application.Shared.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Shared.Persistence;

public class DatabaseSession(Database database, TimeProvider time)
{
	// Waits before the retries of opening a connection; the change itself is never retried.
	private static readonly TimeSpan[] OpenRetryDelays =
		[TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(800)];

	// A fixed number of reads at once, so a burst cannot take every pooled connection; tune if reads queue up.
	private readonly SemaphoreSlim _readSlots = new(Environment.ProcessorCount * 2);

	public async Task<T> ReadAsync<TContext, T>(Func<TContext, Task<T>> query,
		CancellationToken ct = default) where TContext : DbContext, IDatabaseContext<TContext>
	{
		await _readSlots.WaitAsync(ct);
		try
		{
			await using var context = database.CreateReadContext<TContext>();
			return await query(context);
		}
		finally
		{
			_readSlots.Release();
		}
	}

	public async Task WriteAsync<TContext>(Func<TContext, Task> change)
		where TContext : DbContext, IDatabaseContext<TContext>
	{
		await WriteAsync<TContext, bool>(async context =>
		{
			await change(context);
			return true;
		});
	}

	public async Task<T> WriteAsync<TContext, T>(Func<TContext, Task<T>> change)
		where TContext : DbContext, IDatabaseContext<TContext>
	{
		try
		{
			await using var context = await OpenContextAsync<TContext>();
			var result = await change(context);
			await context.SaveChangesAsync();
			return result;
		}
		catch (Exception exception) when (database.IsUniqueViolation(exception))
		{
			throw new AlreadyExistsException("A value that must be unique is already stored.");
		}
	}

	public Task TransactAsync<TContext>(Func<TContext, Task> work)
		where TContext : DbContext, IDatabaseContext<TContext>
	{
		return WriteAsync<TContext>(async context =>
		{
			await using var transaction = await context.Database.BeginTransactionAsync();
			await work(context);
			await context.SaveChangesAsync();
			await transaction.CommitAsync();
		});
	}

	protected virtual Task OpenAsync(DbContext context)
	{
		return context.Database.OpenConnectionAsync();
	}

	private async Task<TContext> OpenContextAsync<TContext>()
		where TContext : DbContext, IDatabaseContext<TContext>
	{
		for (var retry = 0;; retry++)
		{
			var context = database.CreateConfirmedContext<TContext>();
			try
			{
				await OpenAsync(context);
				return context;
			}
			catch (Exception exception) when (retry < OpenRetryDelays.Length &&
			                                  database.IsConnectionFailure(exception))
			{
				await context.DisposeAsync();
				await Task.Delay(OpenRetryDelays[retry], time, CancellationToken.None);
			}
			catch
			{
				await context.DisposeAsync();
				throw;
			}
		}
	}
}