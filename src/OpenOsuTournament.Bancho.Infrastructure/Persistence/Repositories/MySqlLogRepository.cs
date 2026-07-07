using Dapper;
using MySqlConnector;
using OpenOsuTournament.Bancho.Application.Abstractions.Social;

namespace OpenOsuTournament.Bancho.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="ILogRepository" />
public sealed class MySqlLogRepository(string connectionString) : ILogRepository
{
    public async Task CreateAsync(int fromId, int toId, string action, string message,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.ExecuteAsync(
            "INSERT INTO Logs (`From`, `To`, `Action`, Msg, Time) VALUES (@FromId, @ToId, @Action, @Message, UTC_TIMESTAMP())",
            new { FromId = fromId, ToId = toId, Action = action, Message = message });
    }
}
