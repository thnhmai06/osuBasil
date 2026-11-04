using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Basil.Infrastructure.Common.Persistence;

/// <summary>Gives access to the server's database: pooled connections through which data is read and stored.</summary>
public sealed class Database : IAsyncDisposable
{
	private readonly ConcurrentDictionary<Type, object> _readOptions = new();
	private readonly NpgsqlDataSource _readSource;
	private readonly ConcurrentDictionary<Type, object> _synchronousOptions = new();
	private readonly NpgsqlDataSource _synchronousSource;

	/// <summary>Creates the database connections used for reads and for confirmed writes.</summary>
	/// <param name="options">The database connection settings.</param>
	public Database(IOptions<DatabaseOptions> options)
	{
		var settings = options.Value;
		var write = settings.WriteConnectionString;
		var read = string.IsNullOrEmpty(settings.ReadConnectionString) ? write : settings.ReadConnectionString;
		_readSource = NpgsqlDataSource.Create(WithServerOptions(read, "-c idle_in_transaction_session_timeout=30s",
			settings.ReadPoolSize));
		_synchronousSource = NpgsqlDataSource.Create(WithServerOptions(write,
			"-c idle_in_transaction_session_timeout=30s", settings.WritePoolSize));
	}

	public async ValueTask DisposeAsync()
	{
		await _readSource.DisposeAsync();
		await _synchronousSource.DisposeAsync();
	}

	/// <summary>Makes a context for reading, on the connections meant for reads.</summary>
	public TContext CreateReadContext<TContext>()
		where TContext : DbContext, IDatabaseContext<TContext>
	{
		return TContext.Create(OptionsFor<TContext>(_readOptions, _readSource));
	}

	/// <summary>Makes a context whose commits are confirmed before it returns, for writes whose outcome the caller needs.</summary>
	public TContext CreateConfirmedContext<TContext>()
		where TContext : DbContext, IDatabaseContext<TContext>
	{
		return TContext.Create(OptionsFor<TContext>(_synchronousOptions, _synchronousSource));
	}

	private static DbContextOptions<TContext> OptionsFor<TContext>(ConcurrentDictionary<Type, object> cache,
		NpgsqlDataSource source) where TContext : DbContext, IDatabaseContext<TContext>
	{
		return (DbContextOptions<TContext>)cache.GetOrAdd(typeof(TContext), _ => CreateOptions<TContext>(source));
	}

	private static DbContextOptions<TContext> CreateOptions<TContext>(NpgsqlDataSource source)
		where TContext : DbContext, IDatabaseContext<TContext>
	{
		// No retrying execution strategy: DatabaseSession retries only the opening of the connection.
		return new DbContextOptionsBuilder<TContext>()
			.UseNpgsql(source, postgres => postgres.MigrationsHistoryTable(TContext.HistoryTable))
			.UseSnakeCaseNamingConvention()
			.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
			.Options;
	}

	private static string WithServerOptions(string connectionString, string serverOption, int poolSize)
	{
		var builder = new NpgsqlConnectionStringBuilder(connectionString);
		if (poolSize > 0) builder.MaxPoolSize = poolSize;
		builder.Options = string.IsNullOrEmpty(builder.Options) ? serverOption : $"{builder.Options} {serverOption}";
		return builder.ConnectionString;
	}
}