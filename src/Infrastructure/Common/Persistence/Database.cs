using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Common.Persistence;

/// <summary>Gives access to the server's database: pooled connections through which data is read and stored.</summary>
public sealed class Database : IAsyncDisposable
{
	private readonly DatabaseProviders _providers;
	private readonly ConcurrentDictionary<Type, object> _readOptions = new();
	private readonly IDatabaseSource _readSource;
	private readonly ConcurrentDictionary<Type, object> _synchronousOptions = new();
	private readonly IDatabaseSource _synchronousSource;

	/// <summary>Creates the database connections used for reads and for confirmed writes.</summary>
	/// <param name="providers">The database engine of each endpoint.</param>
	/// <param name="options">The database connection settings.</param>
	public Database(DatabaseProviders providers, IOptions<DatabaseOptions> options)
	{
		_providers = providers;
		var settings = options.Value;
		var write = settings.WriteConnectionString;
		var read = string.IsNullOrEmpty(settings.ReadConnectionString) ? write : settings.ReadConnectionString;
		_readSource = providers.Read.CreateSource(read, settings.ReadPoolSize);
		_synchronousSource = providers.Write.CreateSource(write, settings.WritePoolSize);
	}

	public async ValueTask DisposeAsync()
	{
		await _readSource.DisposeAsync();
		await _synchronousSource.DisposeAsync();
	}

	/// <summary>Tells whether a write failed because a value that must be unique is already stored.</summary>
	internal bool IsUniqueViolation(Exception exception)
	{
		return _providers.Write.IsUniqueViolation(exception);
	}

	/// <summary>Tells whether a failure means an endpoint could not be reached.</summary>
	internal bool IsConnectionFailure(Exception exception)
	{
		return _providers.Read.IsConnectionFailure(exception) || _providers.Write.IsConnectionFailure(exception);
	}

	/// <summary>Makes a context for reading, on the connections meant for reads.</summary>
	public TContext CreateReadContext<TContext>()
		where TContext : DbContext, IDatabaseContext<TContext>
	{
		return TContext.Create(OptionsFor<TContext>(_readOptions, _providers.Read, _readSource));
	}

	/// <summary>Makes a context whose commits are confirmed before it returns, for writes whose outcome the caller needs.</summary>
	public TContext CreateConfirmedContext<TContext>()
		where TContext : DbContext, IDatabaseContext<TContext>
	{
		return TContext.Create(OptionsFor<TContext>(_synchronousOptions, _providers.Write, _synchronousSource));
	}

	private DbContextOptions<TContext> OptionsFor<TContext>(ConcurrentDictionary<Type, object> cache,
		IDatabaseProvider provider, IDatabaseSource source) where TContext : DbContext, IDatabaseContext<TContext>
	{
		return (DbContextOptions<TContext>)cache.GetOrAdd(typeof(TContext),
			_ => CreateOptions<TContext>(provider, source));
	}

	private DbContextOptions<TContext> CreateOptions<TContext>(IDatabaseProvider provider, IDatabaseSource source)
		where TContext : DbContext, IDatabaseContext<TContext>
	{
		// No retrying execution strategy: DatabaseSession retries only the opening of the connection.
		var builder = new DbContextOptionsBuilder<TContext>();
		provider.Configure(builder, source, TContext.HistoryTable);
		return builder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options;
	}
}