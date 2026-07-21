using Basil.Application.Abstractions.Users;
using Basil.Domain.Users;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IClientHashRepository" />
public sealed class SqliteClientHashRepository(string connectionString) : IClientHashRepository
{
    public async Task<ClientHash> CreateAsync(int userId, string osuPathMd5, string adapters, string uninstallId,
        string diskSerial, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync(
            """
            INSERT INTO ClientHashes (UserId, OsuPathMd5, Adapters, UninstallId, DiskSerial, LastSeenAt, Occurrences)
            VALUES (@UserId, @OsuPathMd5, @Adapters, @UninstallId, @DiskSerial, datetime('now'), 1)
            ON CONFLICT (UserId, OsuPathMd5, Adapters, UninstallId, DiskSerial)
            DO UPDATE SET LastSeenAt = datetime('now'), Occurrences = Occurrences + 1
            """,
            new
            {
                UserId = userId, OsuPathMd5 = osuPathMd5, Adapters = adapters, UninstallId = uninstallId,
                DiskSerial = diskSerial
            });

        var row = await connection.QuerySingleAsync<ClientHashRow>(
            """
            SELECT * FROM ClientHashes
            WHERE UserId = @UserId AND OsuPathMd5 = @OsuPathMd5 AND Adapters = @Adapters
              AND UninstallId = @UninstallId AND DiskSerial = @DiskSerial
            """,
            new
            {
                UserId = userId, OsuPathMd5 = osuPathMd5, Adapters = adapters, UninstallId = uninstallId,
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
                  SELECT ch.UserId, ch.OsuPathMd5, ch.Adapters, ch.UninstallId,
                         ch.DiskSerial, ch.LastSeenAt, ch.Occurrences,
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

    private SqliteConnection Connect()
    {
        return new SqliteConnection(connectionString);
    }

    private sealed class ClientHashRow
    {
        public int UserId { get; set; }
        public string OsuPathMd5 { get; set; } = "";
        public string Adapters { get; set; } = "";
        public string UninstallId { get; set; } = "";
        public string DiskSerial { get; set; } = "";
        public DateTime LastSeenAt { get; set; }
        public int Occurrences { get; set; }

        public ClientHash ToClientHash()
        {
            return new ClientHash(UserId, OsuPathMd5, Adapters, UninstallId, DiskSerial, LastSeenAt, Occurrences);
        }
    }

    private sealed class ClientHashWithPlayerRow
    {
        public int UserId { get; set; }
        public string OsuPathMd5 { get; set; } = "";
        public string Adapters { get; set; } = "";
        public string UninstallId { get; set; } = "";
        public string DiskSerial { get; set; } = "";
        public DateTime LastSeenAt { get; set; }
        public int Occurrences { get; set; }
        public string Name { get; set; } = "";
        public int Priv { get; set; }

        public ClientHashWithPlayer ToClientHashWithPlayer()
        {
            return new ClientHashWithPlayer(UserId, OsuPathMd5, Adapters, UninstallId, DiskSerial, LastSeenAt,
                Occurrences, Name, (UserPrivileges)Priv);
        }
    }
}
