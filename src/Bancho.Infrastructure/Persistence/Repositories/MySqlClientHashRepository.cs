using Bancho.Application.Abstractions.Users;
using Dapper;
using MySqlConnector;

namespace Bancho.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IClientHashRepository" />
public sealed class MySqlClientHashRepository(string connectionString) : IClientHashRepository
{
    public async Task<ClientHash> CreateAsync(int userId, string osuPath, string adapters, string uninstallId,
        string diskSerial, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync(
            """
            INSERT INTO client_hashes (userid, osupath, adapters, uninstall_id, disk_serial, latest_time, occurrences)
            VALUES (@UserId, @OsuPath, @Adapters, @UninstallId, @DiskSerial, NOW(), 1)
            ON DUPLICATE KEY UPDATE latest_time = NOW(), occurrences = occurrences + 1
            """,
            new
            {
                UserId = userId, OsuPath = osuPath, Adapters = adapters, UninstallId = uninstallId,
                DiskSerial = diskSerial
            });

        var row = await connection.QuerySingleAsync<ClientHashRow>(
            """
            SELECT userid AS UserId, osupath AS OsuPath, adapters, uninstall_id AS UninstallId,
                   disk_serial AS DiskSerial, latest_time AS LatestTime, occurrences
            FROM client_hashes
            WHERE userid = @UserId AND osupath = @OsuPath AND adapters = @Adapters
              AND uninstall_id = @UninstallId AND disk_serial = @DiskSerial
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
                  SELECT ch.userid AS UserId, ch.osupath AS OsuPath, ch.adapters, ch.uninstall_id AS UninstallId,
                         ch.disk_serial AS DiskSerial, ch.latest_time AS LatestTime, ch.occurrences,
                         u.name, u.priv
                  FROM client_hashes ch
                  JOIN users u ON ch.userid = u.id
                  WHERE ch.userid != @UserId
                  """;

        if (runningUnderWine)
        {
            sql += " AND ch.uninstall_id = @UninstallId";
        }
        else
        {
            var oneOf = new List<string> { "ch.adapters = @Adapters", "ch.uninstall_id = @UninstallId" };
            if (diskSerial is not null) oneOf.Add("ch.disk_serial = @DiskSerial");

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