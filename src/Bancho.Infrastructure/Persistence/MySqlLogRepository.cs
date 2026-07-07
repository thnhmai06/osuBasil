using Bancho.Application.Abstractions;
using Dapper;
using MySqlConnector;

namespace Bancho.Infrastructure.Persistence;

/// <inheritdoc cref="ILogRepository" />
public sealed class MySqlLogRepository(string connectionString) : ILogRepository
{
    public async Task CreateAsync(int fromId, int toId, string action, string message, CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.ExecuteAsync(
            "INSERT INTO logs (`from`, `to`, `action`, msg, time) VALUES (@FromId, @ToId, @Action, @Message, UTC_TIMESTAMP())",
            new { FromId = fromId, ToId = toId, Action = action, Message = message });
    }
}
