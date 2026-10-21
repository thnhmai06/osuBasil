using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Basil.Infrastructure.Persistence.Postgres;

/// <summary>Gives access to the server's database: pooled connections through which data is read and stored.</summary>
internal sealed class Database : IAsyncDisposable
{
	private readonly NpgsqlDataSource _asynchronousSource;
	private readonly DbContextOptions<BasilDbContext> _asynchronousOptions;
	private readonly NpgsqlDataSource _synchronousSource;
	private readonly DbContextOptions<BasilDbContext> _synchronousOptions;

	public Database(IOptions<DatabaseOptions> options)
	{
		var connectionString = options.Value.ConnectionString;
		_synchronousSource = NpgsqlDataSource.Create(WithServerOptions(connectionString,
			"-c idle_in_transaction_session_timeout=30s"));
		_asynchronousSource = NpgsqlDataSource.Create(WithServerOptions(connectionString,
			"-c synchronous_commit=off"));
		_synchronousOptions = CreateOptions(_synchronousSource);
		_asynchronousOptions = CreateOptions(_asynchronousSource);
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
	public BasilDbContext CreateSynchronousContext()
	{
		return new BasilDbContext(_synchronousOptions);
	}

	/// <summary>Makes a context whose commits return before the database has flushed them, for copies of memory.</summary>
	public BasilDbContext CreateAsynchronousContext()
	{
		return new BasilDbContext(_asynchronousOptions);
	}

	private static DbContextOptions<BasilDbContext> CreateOptions(NpgsqlDataSource source)
	{
		// No retrying execution strategy: DatabaseWriter retries, in the order the writes of a root were made.
		return new DbContextOptionsBuilder<BasilDbContext>()
			.UseNpgsql(source)
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