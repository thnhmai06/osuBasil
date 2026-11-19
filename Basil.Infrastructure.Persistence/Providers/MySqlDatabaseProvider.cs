using System.Net.Sockets;
using Basil.Infrastructure.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MySql.Data.MySqlClient;

namespace Basil.Infrastructure.Persistence.Providers;

public sealed class MySqlDatabaseProvider : IDatabaseProvider
{
	private const int DuplicateEntry = 1062;

	public IDatabaseSource CreateSource(string connectionString, int poolSize)
	{
		var builder = new MySqlConnectionStringBuilder(connectionString);
		if (poolSize > 0) builder.MaximumPoolSize = (uint)poolSize;
		return new Source(builder.ConnectionString);
	}

	public void Configure(DbContextOptionsBuilder builder, IDatabaseSource source, string historyTable)
	{
		builder.UseMySQL(((Source)source).ConnectionString, mysql => mysql.MigrationsHistoryTable(historyTable)
				.MigrationsAssembly(typeof(MySqlDatabaseProvider).Assembly.GetName().Name))
			.UseSnakeCaseNamingConvention()
			.ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
	}

	public bool IsUniqueViolation(Exception exception)
	{
		return Chain(exception).OfType<MySqlException>().Any(error => error.Number == DuplicateEntry);
	}

	public bool IsConnectionFailure(Exception exception)
	{
		return Chain(exception).Any(error => error is MySqlException { IsTransient: true } or SocketException
			or TimeoutException);
	}

	private static IEnumerable<Exception> Chain(Exception exception)
	{
		for (var current = exception; current is not null; current = current.InnerException)
			yield return current;
	}

	private sealed class Source(string connectionString) : IDatabaseSource
	{
		public string ConnectionString { get; } = connectionString;

		public ConnectionStats? Connections => null;

		public ValueTask DisposeAsync()
		{
			return ValueTask.CompletedTask;
		}
	}
}