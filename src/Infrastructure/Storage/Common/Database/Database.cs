using Basil.Infrastructure.Storage.Common.Options;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Basil.Infrastructure.Storage.Common.Database;

/// <summary>Gives access to the server's database: pooled connections through which data is read and stored.</summary>
internal sealed class Database(IOptions<DatabaseOptions> options) : IAsyncDisposable
{
	internal readonly NpgsqlDataSource SynchronousSource = NpgsqlDataSource.Create(options.Value.ConnectionString);
	internal readonly NpgsqlDataSource AsynchronousSource = CreateAsynchronousSource(options.Value.ConnectionString);

	static Database()
	{
		DefaultTypeMap.MatchNamesWithUnderscores = true;
	}

	public async ValueTask DisposeAsync()
	{
		await SynchronousSource.DisposeAsync();
		await AsynchronousSource.DisposeAsync();
	}

	private static NpgsqlDataSource CreateAsynchronousSource(string connectionString)
	{
		var builder = new NpgsqlConnectionStringBuilder(connectionString);
		builder.Options = string.IsNullOrEmpty(builder.Options)
			? "-c synchronous_commit=off"
			: $"{builder.Options} -c synchronous_commit=off";
		return NpgsqlDataSource.Create(builder.ConnectionString);
	}
}