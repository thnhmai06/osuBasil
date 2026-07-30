using Basil.Application.Abstractions.Social;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="ILogRepository" />
public sealed class SqliteLogRepository(string connectionString, ILogger<SqliteLogRepository> logger) : ILogRepository
{
	public async Task CreateAsync(int fromId, int toId, string action, string message,
		CancellationToken cancellationToken = default)
	{
		await using var connection = new SqliteConnection(connectionString);
		await connection.ExecuteAsync(
			"INSERT INTO Logs (FromId, ToId, Action, Msg, CreatedAt) VALUES (@FromId, @ToId, @Action, @Message, datetime('now'))",
			new { FromId = fromId, ToId = toId, Action = action, Message = message });
		logger.LogDebug("Log entry created: FromId={FromId} ToId={ToId} Action={Action}", fromId, toId, action);
	}
}