using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Basil.Infrastructure.Common.Persistence;

/// <summary>Gives access to the server's database: pooled connections through which data is read and stored.</summary>
public sealed class Database : IAsyncDisposable
{
	private readonly NpgsqlDataSource _asynchronousSource;
	private readonly NpgsqlDataSource _synchronousSource;
	private readonly ConcurrentDictionary<Type, object> _asynchronousOptions = new();
	private readonly ConcurrentDictionary<Type, object> _synchronousOptions = new();

	/// <summary>Creates the database connections used for confirmed and unconfirmed writes.</summary>
	/// <param name="options">The database connection settings.</param>
	public Database(IOptions<DatabaseOptions> options)
	{
		var connectionString = options.Value.ConnectionString;
		_synchronousSource = NpgsqlDataSource.Create(WithServerOptions(connectionString,
			"-c idle_in_transaction_session_timeout=30s"));
		_asynchronousSource = NpgsqlDataSource.Create(WithServerOptions(connectionString,
			"-c synchronous_commit=off"));
	}

	public async ValueTask DisposeAsync()
	{
		await _synchronousSource.DisposeAsync();
		await _asynchronousSource.DisposeAsync();
	}

	/// <summary>
	///     Makes a context whose commits are confirmed before it returns, for reads and for writes whose outcome the
	///     caller needs.
	/// </summary>
	public TContext CreateConfirmedContext<TContext>()
		where TContext : DbContext, IDatabaseContext<TContext>
	{
		return TContext.Create(OptionsFor<TContext>(_synchronousOptions, _synchronousSource));
	}

	/// <summary>Makes a context whose commits return before the database has flushed them, for copies of memory.</summary>
	public TContext CreateUnconfirmedContext<TContext>()
		where TContext : DbContext, IDatabaseContext<TContext>
	{
		return TContext.Create(OptionsFor<TContext>(_asynchronousOptions, _asynchronousSource));
	}

	private static DbContextOptions<TContext> OptionsFor<TContext>(ConcurrentDictionary<Type, object> cache,
		NpgsqlDataSource source) where TContext : DbContext, IDatabaseContext<TContext>
	{
		return (DbContextOptions<TContext>)cache.GetOrAdd(typeof(TContext), _ => CreateOptions<TContext>(source));
	}

	private static DbContextOptions<TContext> CreateOptions<TContext>(NpgsqlDataSource source)
		where TContext : DbContext, IDatabaseContext<TContext>
	{
		// No retrying execution strategy: DatabaseWriter retries, in the order the writes of a root were made.
		return new DbContextOptionsBuilder<TContext>()
			.UseNpgsql(source, postgres => postgres.MigrationsHistoryTable(TContext.HistoryTable))
			.UseSnakeCaseNamingConvention()
			.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
			.Options;
	}

	private static string WithServerOptions(string connectionString, string serverOption)
	{
		var builder = new NpgsqlConnectionStringBuilder(connectionString);
		builder.Options = string.IsNullOrEmpty(builder.Options) ? serverOption : $"{builder.Options} {serverOption}";
		return builder.ConnectionString;
	}
}