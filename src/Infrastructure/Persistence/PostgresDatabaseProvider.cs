using System.Net.Sockets;
using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Basil.Infrastructure.Persistence;

/// <summary>The PostgreSQL engine of the server: pooled Npgsql connections, snake_case names and the migrations of this assembly.</summary>
public sealed class PostgresDatabaseProvider : IDatabaseProvider
{
	private const string ServerOption = "-c idle_in_transaction_session_timeout=30s";

	/// <inheritdoc />
	public IDatabaseSource CreateSource(string connectionString, int poolSize)
	{
		var builder = new NpgsqlConnectionStringBuilder(connectionString);
		if (poolSize > 0) builder.MaxPoolSize = poolSize;
		builder.Options = string.IsNullOrEmpty(builder.Options) ? ServerOption : $"{builder.Options} {ServerOption}";
		return new Source(NpgsqlDataSource.Create(builder.ConnectionString));
	}

	/// <inheritdoc />
	public void Configure(DbContextOptionsBuilder builder, IDatabaseSource source, string historyTable)
	{
		builder.UseNpgsql(((Source)source).DataSource, postgres => Migrations(postgres, historyTable))
			.UseSnakeCaseNamingConvention();
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

	private sealed class Source(NpgsqlDataSource dataSource) : IDatabaseSource
	{
		public NpgsqlDataSource DataSource { get; } = dataSource;

		public ValueTask DisposeAsync()
		{
			return DataSource.DisposeAsync();
		}
	}
}
