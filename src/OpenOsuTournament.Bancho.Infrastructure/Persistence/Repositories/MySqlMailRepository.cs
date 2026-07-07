using Dapper;
using MySqlConnector;
using OpenOsuTournament.Bancho.Application.Abstractions.Social;

namespace OpenOsuTournament.Bancho.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IMailRepository" />
public sealed class MySqlMailRepository(string connectionString) : IMailRepository
{
    public async Task<Mail> CreateAsync(int fromId, int toId, string msg, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var id = await connection.ExecuteScalarAsync<int>(
            """
            INSERT INTO mail (from_id, to_id, msg, time) VALUES (@FromId, @ToId, @Msg, UNIX_TIMESTAMP());
            SELECT LAST_INSERT_ID();
            """,
            new { FromId = fromId, ToId = toId, Msg = msg });

        var row = await connection.QuerySingleAsync<MailRow>(
            "SELECT id, from_id AS FromId, to_id AS ToId, msg, time, `read` FROM mail WHERE id = @Id",
            new { Id = id });

        return row.ToMail();
    }

    public async Task<IReadOnlyList<MailWithUsernames>> FetchUnreadMailToUserAsync(int userId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var rows = await connection.QueryAsync<MailWithUsernamesRow>(
            """
            SELECT m.id, m.from_id AS FromId, m.to_id AS ToId, m.msg, m.time, m.`read`,
                   fu.name AS FromName, tu.name AS ToName
            FROM mail m
            JOIN users fu ON fu.id = m.from_id
            JOIN users tu ON tu.id = m.to_id
            WHERE m.to_id = @UserId AND m.`read` = 0
            """,
            new { UserId = userId });

        return rows.Select(r => r.ToMailWithUsernames()).ToList();
    }

    public async Task<IReadOnlyList<Mail>> MarkConversationAsReadAsync(int toId, int fromId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var rows = (await connection.QueryAsync<MailRow>(
            "SELECT id, from_id AS FromId, to_id AS ToId, msg, time, `read` FROM mail WHERE to_id = @ToId AND from_id = @FromId AND `read` = 0",
            new { ToId = toId, FromId = fromId })).ToList();

        if (rows.Count == 0) return [];

        await connection.ExecuteAsync(
            "UPDATE mail SET `read` = 1 WHERE to_id = @ToId AND from_id = @FromId AND `read` = 0",
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