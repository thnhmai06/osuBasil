using Basil.Application.Abstractions.Social;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IUserLogRepository" />
/// <remarks>
///     A single append-only insert into the UserLogs table, with the timestamp produced in SQL via
///     <c>datetime('now')</c>.
/// </remarks>
public sealed class SqliteUserLogRepository(string connectionString, ILogger<SqliteUserLogRepository> logger)
	: IUserLogRepository
{
	/// <inheritdoc />
	public async Task CreateAsync(int fromId, int toId, string action, string message,
		CancellationToken cancellationToken = default)
	{
		await using var connection = SqliteConnectionFactory.Open(connectionString);
		await connection.ExecuteAsync(
			"INSERT INTO UserLogs (FromId, ToId, Action, Msg, CreatedAt) VALUES (@FromId, @ToId, @Action, @Message, datetime('now'))",
			new { FromId = fromId, ToId = toId, Action = action, Message = message });
		logger.LogDebug("Log entry created: FromId={FromId} ToId={ToId} Action={Action}", fromId, toId, action);
	}
}