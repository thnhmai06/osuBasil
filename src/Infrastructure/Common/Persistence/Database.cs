using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Common.Persistence;

/// <summary>Gives access to the server's database: pooled connections through which data is read and stored.</summary>
public sealed class Database : IAsyncDisposable
{
	private readonly ConcurrentDictionary<Type, object> _readOptions = new();
	private readonly IDatabaseSource _readSource;
	private readonly ConcurrentDictionary<Type, object> _synchronousOptions = new();
	private readonly IDatabaseSource _synchronousSource;

	/// <summary>Creates the database connections used for reads and for confirmed writes.</summary>
	/// <param name="provider">The database engine.</param>
	/// <param name="options">The database connection settings.</param>
	public Database(IDatabaseProvider provider, IOptions<DatabaseOptions> options)
	{
		Provider = provider;
		var settings = options.Value;
		var write = settings.WriteConnectionString;
		var read = string.IsNullOrEmpty(settings.ReadConnectionString) ? write : settings.ReadConnectionString;
		_readSource = provider.CreateSource(read, settings.ReadPoolSize);
		_synchronousSource = provider.CreateSource(write, settings.WritePoolSize);
	}

	/// <summary>Gets the database engine, which tells the failures of a write apart.</summary>
	internal IDatabaseProvider Provider { get; }

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

	private DbContextOptions<TContext> OptionsFor<TContext>(ConcurrentDictionary<Type, object> cache,
		IDatabaseSource source) where TContext : DbContext, IDatabaseContext<TContext>
	{
		return (DbContextOptions<TContext>)cache.GetOrAdd(typeof(TContext), _ => CreateOptions<TContext>(source));
	}

	private DbContextOptions<TContext> CreateOptions<TContext>(IDatabaseSource source)
		where TContext : DbContext, IDatabaseContext<TContext>
	{
		// No retrying execution strategy: DatabaseSession retries only the opening of the connection.
		var builder = new DbContextOptionsBuilder<TContext>();
		Provider.Configure(builder, source, TContext.HistoryTable);
		return builder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options;
	}
}