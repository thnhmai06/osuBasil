using Basil.Application.Abstractions.Users;
using Dapper;
using MySqlConnector;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IClientHashRepository" />
public sealed class MySqlClientHashRepository(string connectionString) : IClientHashRepository
{
    public async Task<ClientHash> CreateAsync(int userId, string osuPath, string adapters, string uninstallId,
        string diskSerial, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync(
            """
            INSERT INTO ClientHashes (UserId, OsuPath, Adapters, UninstallId, DiskSerial, LatestTime, Occurrences)
            VALUES (@UserId, @OsuPath, @Adapters, @UninstallId, @DiskSerial, NOW(), 1)
            ON DUPLICATE KEY UPDATE LatestTime = NOW(), Occurrences = Occurrences + 1
            """,
            new
            {
                UserId = userId, OsuPath = osuPath, Adapters = adapters, UninstallId = uninstallId,
                DiskSerial = diskSerial
            });

        var row = await connection.QuerySingleAsync<ClientHashRow>(
            """
            SELECT * FROM ClientHashes
            WHERE UserId = @UserId AND OsuPath = @OsuPath AND Adapters = @Adapters
              AND UninstallId = @UninstallId AND DiskSerial = @DiskSerial
            """,
            new
            {
                UserId = userId, OsuPath = osuPath, Adapters = adapters, UninstallId = uninstallId,
                DiskSerial = diskSerial
            });

        return row.ToClientHash();
    }

    public async Task<IReadOnlyList<ClientHashWithPlayer>> FetchAnyHardwareMatchesForUserAsync(
        int userId,
        bool runningUnderWine,
        string adapters,
        string uninstallId,
        string? diskSerial,
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();

        var sql = """
                  SELECT ch.UserId, ch.OsuPath, ch.Adapters, ch.UninstallId,
                         ch.DiskSerial, ch.LatestTime, ch.Occurrences,
                         u.Name, u.Priv
                  FROM ClientHashes ch
                  JOIN Users u ON ch.UserId = u.Id
                  WHERE ch.UserId != @UserId
                  """;

        if (runningUnderWine)
        {
            sql += " AND ch.UninstallId = @UninstallId";
        }
        else
        {
            var oneOf = new List<string> { "ch.Adapters = @Adapters", "ch.UninstallId = @UninstallId" };
            if (diskSerial is not null) oneOf.Add("ch.DiskSerial = @DiskSerial");

            sql += $" AND ({string.Join(" OR ", oneOf)})";
        }

        var rows = await connection.QueryAsync<ClientHashWithPlayerRow>(
            sql,
            new { UserId = userId, Adapters = adapters, UninstallId = uninstallId, DiskSerial = diskSerial });

        return rows.Select(r => r.ToClientHashWithPlayer()).ToList();
    }

    private MySqlConnection Connect()
    {
        return new MySqlConnection(connectionString);
    }

    private sealed class ClientHashRow
    {
        public int UserId { get; set; }
        public string OsuPath { get; set; } = "";
        public string Adapters { get; set; } = "";
        public string UninstallId { get; set; } = "";
        public string DiskSerial { get; set; } = "";
        public DateTime LatestTime { get; set; }
        public int Occurrences { get; set; }

        public ClientHash ToClientHash()
        {
            return new ClientHash(UserId, OsuPath, Adapters, UninstallId, DiskSerial, LatestTime, Occurrences);
        }
    }

    private sealed class ClientHashWithPlayerRow
    {
        public int UserId { get; set; }
        public string OsuPath { get; set; } = "";
        public string Adapters { get; set; } = "";
        public string UninstallId { get; set; } = "";
        public string DiskSerial { get; set; } = "";
        public DateTime LatestTime { get; set; }
        public int Occurrences { get; set; }
        public string Name { get; set; } = "";
        public int Priv { get; set; }

        public ClientHashWithPlayer ToClientHashWithPlayer()
        {
            return new ClientHashWithPlayer(UserId, OsuPath, Adapters, UninstallId, DiskSerial, LatestTime, Occurrences,
                Name, Priv);
        }
    }
}