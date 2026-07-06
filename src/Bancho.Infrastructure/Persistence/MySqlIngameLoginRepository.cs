using Bancho.Application.Abstractions;
using Dapper;
using MySqlConnector;

namespace Bancho.Infrastructure.Persistence;

/// <inheritdoc cref="IIngameLoginRepository" />
public sealed class MySqlIngameLoginRepository(string connectionString) : IIngameLoginRepository
{
    private sealed class IngameLoginRow
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Ip { get; set; } = "";
        public DateTime OsuVer { get; set; }
        public string OsuStream { get; set; } = "";
        public DateTime Datetime { get; set; }

        public IngameLogin ToIngameLogin() => new(Id, UserId, Ip, DateOnly.FromDateTime(OsuVer), OsuStream, Datetime);
    }

    private MySqlConnection Connect() => new(connectionString);

    public async Task<IngameLogin> CreateAsync(int userId, string ip, DateOnly osuVer, string osuStream, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var id = await connection.ExecuteScalarAsync<int>(
            """
            INSERT INTO ingame_logins (userid, ip, osu_ver, osu_stream, datetime)
            VALUES (@UserId, @Ip, @OsuVer, @OsuStream, NOW());
            SELECT LAST_INSERT_ID();
            """,
            new { UserId = userId, Ip = ip, OsuVer = osuVer.ToDateTime(TimeOnly.MinValue), OsuStream = osuStream });

        var row = await connection.QuerySingleAsync<IngameLoginRow>(
            """
            SELECT id, userid AS UserId, ip, osu_ver AS OsuVer, osu_stream AS OsuStream, datetime
            FROM ingame_logins WHERE id = @Id
            """,
            new { Id = id });

        return row.ToIngameLogin();
    }
}
