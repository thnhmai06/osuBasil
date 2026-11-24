using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Basil.Infrastructure.Shared.Persistence;

public readonly record struct ConnectionStats(int InUse, int Idle);

public sealed class Database : IAsyncDisposable
{
	private const string ServerOption = "-c idle_in_transaction_session_timeout=30s";

	// The migrations live in the Persistence project, which this one cannot reference.
	private const string MigrationsAssembly = "Basil.Infrastructure.Persistence";

	private readonly ConcurrentDictionary<Type, object> _readOptions = new();
	private readonly Source _readSource;
	private readonly ConcurrentDictionary<Type, object> _synchronousOptions = new();
	private readonly Source _synchronousSource;

	public Database(IOptions<DatabaseOptions> options)
	{
		var settings = options.Value;
		var write = settings.WriteConnectionString;
		var read = string.IsNullOrEmpty(settings.ReadConnectionString) ? write : settings.ReadConnectionString;
		_readSource = CreateSource(read, settings.ReadPoolSize);
		_synchronousSource = CreateSource(write, settings.WritePoolSize);
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
		return Chain(exception).OfType<PostgresException>()
			.Any(error => error.SqlState == PostgresErrorCodes.UniqueViolation);
	}

	internal bool IsConnectionFailure(Exception exception)
	{
		return Chain(exception).Any(error => error is NpgsqlException { IsTransient: true } or SocketException
			or TimeoutException);
	}

	public TContext CreateReadContext<TContext>()
		where TContext : DbContext, IDatabaseContext<TContext>
	{
		return TContext.Create(OptionsFor<TContext>(_readOptions, _readSource));
	}

	public TContext CreateConfirmedContext<TContext>()
		where TContext : DbContext, IDatabaseContext<TContext>
	{
		return TContext.Create(OptionsFor<TContext>(_synchronousOptions, _synchronousSource));
	}

	private DbContextOptions<TContext> OptionsFor<TContext>(ConcurrentDictionary<Type, object> cache,
		Source source) where TContext : DbContext, IDatabaseContext<TContext>
	{
		return (DbContextOptions<TContext>)cache.GetOrAdd(typeof(TContext),
			_ => CreateOptions<TContext>(source));
	}

	private DbContextOptions<TContext> CreateOptions<TContext>(Source source)
		where TContext : DbContext, IDatabaseContext<TContext>
	{
		// No retrying execution strategy: DatabaseSession retries only the opening of the connection.
		var builder = new DbContextOptionsBuilder<TContext>();
		builder.UseNpgsql(source.DataSource,
				postgres => postgres.MigrationsHistoryTable(TContext.HistoryTable).MigrationsAssembly(MigrationsAssembly))
			.UseSnakeCaseNamingConvention();
		return builder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).AddInterceptors(Metrics).Options;
	}

	private static Source CreateSource(string connectionString, int poolSize)
	{
		var builder = new NpgsqlConnectionStringBuilder(connectionString);
		if (poolSize > 0) builder.MaxPoolSize = poolSize;
		builder.Options = string.IsNullOrEmpty(builder.Options) ? ServerOption : $"{builder.Options} {ServerOption}";
		return new Source(builder.ConnectionString);
	}

	private static IEnumerable<Exception> Chain(Exception exception)
	{
		for (var current = exception; current is not null; current = current.InnerException)
			yield return current;
	}

	private sealed class Source : IAsyncDisposable
	{
		private readonly MeterListener _listener = new();
		private readonly Lock _measuring = new();
		private readonly string _name = Guid.NewGuid().ToString("N");
		private int _idle;
		private int _inUse;

		public Source(string connectionString)
		{
			DataSource = new NpgsqlDataSourceBuilder(connectionString) { Name = _name }.Build();
			// Npgsql 10 publishes its pool sizes only as the observable instrument of its meter.
			_listener.InstrumentPublished = (instrument, listener) =>
			{
				if (instrument.Meter.Name == "Npgsql" && instrument.Name == "db.client.connection.count")
					listener.EnableMeasurementEvents(instrument);
			};
			_listener.SetMeasurementEventCallback<int>(OnCount);
			_listener.Start();
		}

		public NpgsqlDataSource DataSource { get; }

		public ConnectionStats? Connections
		{
			get
			{
				lock (_measuring)
				{
					_idle = _inUse = 0;
					_listener.RecordObservableInstruments();
					return new ConnectionStats(_inUse, _idle);
				}
			}
		}

		public ValueTask DisposeAsync()
		{
			_listener.Dispose();
			return DataSource.DisposeAsync();
		}

		private void OnCount(Instrument instrument, int value, ReadOnlySpan<KeyValuePair<string, object?>> tags,
			object? state)
		{
			string? pool = null, kind = null;
			foreach (var tag in tags)
				if (tag.Key == "db.client.connection.pool.name") pool = tag.Value as string;
				else if (tag.Key == "db.client.connection.state") kind = tag.Value as string;

			if (pool != _name) return;
			if (kind == "idle") _idle += value;
			else if (kind == "used") _inUse += value;
		}
	}
}