using Dapper;
using MySqlConnector;
using OpenOsuTournament.Bancho.Application.Abstractions.Users;

namespace OpenOsuTournament.Bancho.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IIngameLoginRepository" />
public sealed class MySqlIngameLoginRepository(string connectionString) : IIngameLoginRepository
{
    public async Task<IngameLogin> CreateAsync(int userId, string ip, DateOnly osuVer, string osuStream,
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var id = await connection.ExecuteScalarAsync<int>(
            """
            INSERT INTO IngameLogins (UserId, Ip, OsuVer, OsuStream, Datetime)
            VALUES (@UserId, @Ip, @OsuVer, @OsuStream, NOW());
            SELECT LAST_INSERT_ID();
            """,
            new { UserId = userId, Ip = ip, OsuVer = osuVer.ToDateTime(TimeOnly.MinValue), OsuStream = osuStream });

        var row = await connection.QuerySingleAsync<IngameLoginRow>(
            "SELECT * FROM IngameLogins WHERE Id = @Id",
            new { Id = id });

        return row.ToIngameLogin();
    }

    private MySqlConnection Connect()
    {
        return new MySqlConnection(connectionString);
    }

    private sealed class IngameLoginRow
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Ip { get; set; } = "";
        public DateTime OsuVer { get; set; }
        public string OsuStream { get; set; } = "";
        public DateTime Datetime { get; set; }

        public IngameLogin ToIngameLogin()
        {
            return new IngameLogin(Id, UserId, Ip, DateOnly.FromDateTime(OsuVer), OsuStream, Datetime);
        }
    }
}
