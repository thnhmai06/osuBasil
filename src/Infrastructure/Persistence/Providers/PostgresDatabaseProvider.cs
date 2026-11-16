using System.Diagnostics.Metrics;
using System.Net.Sockets;
using Basil.Infrastructure.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Basil.Infrastructure.Persistence.Providers;

/// <summary>
///     The PostgreSQL database engine of the server.
/// </summary>
public sealed class PostgresDatabaseProvider : IDatabaseProvider
{
	private const string ServerOption = "-c idle_in_transaction_session_timeout=30s";

	/// <inheritdoc />
	public IDatabaseSource CreateSource(string connectionString, int poolSize)
	{
		var builder = new NpgsqlConnectionStringBuilder(connectionString);
		if (poolSize > 0) builder.MaxPoolSize = poolSize;
		builder.Options = string.IsNullOrEmpty(builder.Options) ? ServerOption : $"{builder.Options} {ServerOption}";
		return new Source(builder.ConnectionString);
	}

	/// <inheritdoc />
	public void Configure(DbContextOptionsBuilder builder, IDatabaseSource source, string historyTable)
	{
		// The migrations carry no store types or provider annotations, so their snapshot never equals the provider's model.
		builder.UseNpgsql(((Source)source).DataSource, postgres => Migrations(postgres, historyTable))
			.UseSnakeCaseNamingConvention()
			.ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
	}

	/// <inheritdoc />
	public bool IsUniqueViolation(Exception exception)
	{
		return Chain(exception).OfType<PostgresException>()
			.Any(error => error.SqlState == PostgresErrorCodes.UniqueViolation);
	}

	/// <inheritdoc />
	public bool IsConnectionFailure(Exception exception)
	{
		return Chain(exception).Any(error => error is NpgsqlException { IsTransient: true } or SocketException
			or TimeoutException);
	}

	/// <summary>Configures a context for design-time tools, over a placeholder connection that they replace.</summary>
	internal static TContext CreateDesignTime<TContext>() where TContext : DbContext, IDatabaseContext<TContext>
	{
		var builder = new DbContextOptionsBuilder<TContext>();
		builder.UseNpgsql("Host=localhost;Database=basil", postgres => Migrations(postgres, TContext.HistoryTable))
			.UseSnakeCaseNamingConvention();
		return TContext.Create(builder.Options);
	}

	private static void Migrations(NpgsqlDbContextOptionsBuilder postgres, string historyTable)
	{
		postgres.MigrationsHistoryTable(historyTable)
			.MigrationsAssembly(typeof(PostgresDatabaseProvider).Assembly.GetName().Name);
	}

	private static IEnumerable<Exception> Chain(Exception exception)
	{
		for (var current = exception; current is not null; current = current.InnerException)
			yield return current;
	}

	private sealed class Source : IDatabaseSource
	{
		private readonly MeterListener _listener = new();
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
				lock (_listener)
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
