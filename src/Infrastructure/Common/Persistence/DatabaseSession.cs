using Basil.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Common.Persistence;

/// <summary>
///     Runs the reads and the awaited writes of a repository, each on a context of its own; a write that fails reaches
///     the caller.
/// </summary>
/// <param name="database">The database that creates the contexts.</param>
/// <param name="time">The clock that paces the attempts to open a connection.</param>
public class DatabaseSession(Database database, TimeProvider time)
{
	// Waits before the retries of opening a connection; the change itself is never retried.
	private static readonly TimeSpan[] OpenRetryDelays =
		[TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(800)];

	// A fixed number of reads at once, so a burst cannot take every pooled connection; tune if reads queue up.
	private readonly SemaphoreSlim _readSlots = new(Environment.ProcessorCount * 2);

	/// <summary>Runs a query on a context of its own; it sees what is committed.</summary>
	/// <typeparam name="TContext">The context of the module that owns the data.</typeparam>
	/// <typeparam name="T">The type of the result.</typeparam>
	/// <param name="query">The query to run.</param>
	/// <param name="cancellationToken">A token that cancels the read.</param>
	/// <returns>The result of the query.</returns>
	public async Task<T> ReadAsync<TContext, T>(Func<TContext, Task<T>> query,
		CancellationToken cancellationToken = default) where TContext : DbContext, IDatabaseContext<TContext>
	{
		await _readSlots.WaitAsync(cancellationToken);
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

	/// <summary>
	///     Runs a change on a context of its own and stores it before returning; nothing is queued. Opening the
	///     connection is retried a few times when the database is briefly unreachable; the change itself runs once.
	/// </summary>
	/// <typeparam name="TContext">The context of the module that owns the data.</typeparam>
	/// <param name="change">The change; entities it adds or modifies on the context are saved after it runs.</param>
	/// <param name="cancellationToken">A token that cancels the write.</param>
	/// <exception cref="AlreadyExistsException">The change stores a value that must be unique and is already stored.</exception>
	public async Task WriteAsync<TContext>(Func<TContext, CancellationToken, Task> change,
		CancellationToken cancellationToken = default)
		where TContext : DbContext, IDatabaseContext<TContext>
	{
		await WriteAsync<TContext, bool>(async (context, token) =>
		{
			await change(context, token);
			return true;
		}, cancellationToken);
	}

	/// <summary>Runs a change like <see cref="WriteAsync{TContext}" /> and gives back what the change returned.</summary>
	/// <typeparam name="TContext">The context of the module that owns the data.</typeparam>
	/// <typeparam name="T">The type of the result.</typeparam>
	/// <param name="change">The change; entities it adds or modifies on the context are saved after it runs.</param>
	/// <param name="cancellationToken">A token that cancels the write.</param>
	/// <returns>The result of the change.</returns>
	/// <exception cref="AlreadyExistsException">The change stores a value that must be unique and is already stored.</exception>
	public async Task<T> WriteAsync<TContext, T>(Func<TContext, CancellationToken, Task<T>> change,
		CancellationToken cancellationToken = default)
		where TContext : DbContext, IDatabaseContext<TContext>
	{
		try
		{
			await using var context = await OpenContextAsync<TContext>(cancellationToken);
			var result = await change(context, cancellationToken);
			await context.SaveChangesAsync(cancellationToken);
			return result;
		}
		catch (Exception exception) when (exception.IsUniqueViolation())
		{
			throw new AlreadyExistsException("A value that must be unique is already stored.");
		}
	}

	/// <summary>Runs the changes of one business operation in a single transaction: all are stored or none is.</summary>
	/// <typeparam name="TContext">The context of the module that owns the data.</typeparam>
	/// <param name="work">The changes; entities it adds or modifies on the context are saved after it runs.</param>
	/// <param name="cancellationToken">A token that cancels the transaction.</param>
	/// <exception cref="AlreadyExistsException">A change stores a value that must be unique and is already stored.</exception>
	public Task TransactAsync<TContext>(Func<TContext, CancellationToken, Task> work,
		CancellationToken cancellationToken = default)
		where TContext : DbContext, IDatabaseContext<TContext>
	{
		return WriteAsync<TContext>(async (context, token) =>
		{
			await using var transaction = await context.Database.BeginTransactionAsync(token);
			await work(context, token);
			await context.SaveChangesAsync(token);
			await transaction.CommitAsync(token);
		}, cancellationToken);
	}

	/// <summary>Opens the connection of a context; a test replaces it to make the database unreachable.</summary>
	/// <param name="context">The context whose connection is opened.</param>
	/// <param name="cancellationToken">A token that cancels the opening.</param>
	protected virtual Task OpenAsync(DbContext context, CancellationToken cancellationToken)
	{
		return context.Database.OpenConnectionAsync(cancellationToken);
	}

	private async Task<TContext> OpenContextAsync<TContext>(CancellationToken cancellationToken)
		where TContext : DbContext, IDatabaseContext<TContext>
	{
		for (var retry = 0;; retry++)
		{
			var context = database.CreateConfirmedContext<TContext>();
			try
			{
				await OpenAsync(context, cancellationToken);
				return context;
			}
			catch (Exception) when (!cancellationToken.IsCancellationRequested && retry < OpenRetryDelays.Length)
			{
				await context.DisposeAsync();
				await Task.Delay(OpenRetryDelays[retry], time, cancellationToken);
			}
			catch
			{
				await context.DisposeAsync();
				throw;
			}
		}
	}
}