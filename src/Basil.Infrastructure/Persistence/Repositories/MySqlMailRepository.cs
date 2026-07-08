using Basil.Application.Abstractions.Social;
using Dapper;
using MySqlConnector;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IMailRepository" />
public sealed class MySqlMailRepository(string connectionString) : IMailRepository
{
    public async Task<Mail> CreateAsync(int fromId, int toId, string msg, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var id = await connection.ExecuteScalarAsync<int>(
            """
            INSERT INTO Mail (FromId, ToId, Msg, Time) VALUES (@FromId, @ToId, @Msg, UNIX_TIMESTAMP());
            SELECT LAST_INSERT_ID();
            """,
            new { FromId = fromId, ToId = toId, Msg = msg });

        var row = await connection.QuerySingleAsync<MailRow>(
            "SELECT Id, FromId, ToId, Msg, Time, `Read` FROM Mail WHERE Id = @Id",
            new { Id = id });

        return row.ToMail();
    }

    public async Task<IReadOnlyList<MailWithUsernames>> FetchUnreadMailToUserAsync(int userId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var rows = await connection.QueryAsync<MailWithUsernamesRow>(
            """
            SELECT m.Id, m.FromId, m.ToId, m.Msg, m.Time, m.`Read`,
                   fu.Name AS FromName, tu.Name AS ToName
            FROM Mail m
            JOIN Users fu ON fu.Id = m.FromId
            JOIN Users tu ON tu.Id = m.ToId
            WHERE m.ToId = @UserId AND m.`Read` = 0
            """,
            new { UserId = userId });

        return rows.Select(r => r.ToMailWithUsernames()).ToList();
    }

    public async Task<IReadOnlyList<Mail>> MarkConversationAsReadAsync(int toId, int fromId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var rows = (await connection.QueryAsync<MailRow>(
            "SELECT Id, FromId, ToId, Msg, Time, `Read` FROM Mail WHERE ToId = @ToId AND FromId = @FromId AND `Read` = 0",
            new { ToId = toId, FromId = fromId })).ToList();

        if (rows.Count == 0) return [];

        await connection.ExecuteAsync(
            "UPDATE Mail SET `Read` = 1 WHERE ToId = @ToId AND FromId = @FromId AND `Read` = 0",
            new { ToId = toId, FromId = fromId });

        return rows.Select(r => r.ToMail()).ToList();
    }

    private MySqlConnection Connect()
    {
        return new MySqlConnection(connectionString);
    }

    private sealed class MailRow
    {
        public int Id { get; set; }
        public int FromId { get; set; }
        public int ToId { get; set; }
        public string Msg { get; set; } = "";
        public int? Time { get; set; }
        public bool Read { get; set; }

        public Mail ToMail()
        {
            return new Mail(Id, FromId, ToId, Msg, Time, Read);
        }
    }

    private sealed class MailWithUsernamesRow
    {
        public int Id { get; set; }
        public int FromId { get; set; }
        public int ToId { get; set; }
        public string Msg { get; set; } = "";
        public int? Time { get; set; }
        public bool Read { get; set; }
        public string FromName { get; set; } = "";
        public string ToName { get; set; } = "";

        public MailWithUsernames ToMailWithUsernames()
        {
            return new MailWithUsernames(Id, FromId, ToId, Msg, Time, Read, FromName, ToName);
        }
    }
}