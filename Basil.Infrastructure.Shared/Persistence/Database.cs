using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Shared.Persistence;

public sealed class Database : IAsyncDisposable
{
	private readonly IDatabaseProvider _provider;
	private readonly ConcurrentDictionary<Type, object> _readOptions = new();
	private readonly IDatabaseSource _readSource;
	private readonly ConcurrentDictionary<Type, object> _synchronousOptions = new();
	private readonly IDatabaseSource _synchronousSource;

	public Database(IDatabaseProvider provider, IOptions<DatabaseOptions> options)
	{
		_provider = provider;
		var settings = options.Value;
		var write = settings.WriteConnectionString;
		var read = string.IsNullOrEmpty(settings.ReadConnectionString) ? write : settings.ReadConnectionString;
		_readSource = provider.CreateSource(read, settings.ReadPoolSize);
		_synchronousSource = provider.CreateSource(write, settings.WritePoolSize);
	}

	public DatabaseMetrics Metrics { get; } = new();

	public ConnectionStats? Connections
	{
		get
		{
			if (_readSource.Connections is not { } read || _synchronousSource.Connections is not { } write) return null;
			return new ConnectionStats(read.InUse + write.InUse, read.Idle + write.Idle);
		}
	}

	public async ValueTask DisposeAsync()
	{
		await _readSource.DisposeAsync();
		await _synchronousSource.DisposeAsync();
	}

	internal bool IsUniqueViolation(Exception exception)
	{
		return _provider.IsUniqueViolation(exception);
	}

	internal bool IsConnectionFailure(Exception exception)
	{
		return _provider.IsConnectionFailure(exception);
	}

	public TContext CreateReadContext<TContext>()
		where TContext : DbContext, IDatabaseContext<TContext>
	{
		return TContext.Create(OptionsFor<TContext>(_readOptions, _provider, _readSource));
	}

	public TContext CreateConfirmedContext<TContext>()
		where TContext : DbContext, IDatabaseContext<TContext>
	{
		return TContext.Create(OptionsFor<TContext>(_synchronousOptions, _provider, _synchronousSource));
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
		return builder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).AddInterceptors(Metrics).Options;
	}
}