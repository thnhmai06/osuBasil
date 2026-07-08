using Basil.Application.Abstractions.Users;
using Basil.Domain.Users;
using Dapper;
using MySqlConnector;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IUserRepository" />
public sealed class MySqlUserRepository(string connectionString) : IUserRepository
{
    public async Task<User?> FetchByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var row = await connection.QuerySingleOrDefaultAsync<UserRow>(
            "SELECT * FROM Users WHERE Id = @Id",
            new { Id = id });
        return row?.ToUser();
    }

    public async Task<User?> FetchByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var row = await connection.QuerySingleOrDefaultAsync<UserRow>(
            "SELECT * FROM Users WHERE SafeName = @SafeName",
            new { SafeName = SafeName.Make(name) });
        return row?.ToUser();
    }

    public async Task<string?> FetchPasswordHashAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        return await connection.QuerySingleOrDefaultAsync<string>(
            "SELECT PwBcrypt FROM Users WHERE Id = @Id",
            new { Id = id });
    }

    public async Task UpdateCountryAsync(int id, string country, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync(
            "UPDATE Users SET Country = @Country WHERE Id = @Id",
            new { Id = id, Country = country });
    }

    public async Task UpdatePrivilegesAsync(int id, int priv, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync(
            "UPDATE Users SET Priv = @Priv WHERE Id = @Id",
            new { Id = id, Priv = priv });
    }

    public async Task UpdateNameAsync(int id, string name, string safeName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync(
            "UPDATE Users SET Name = @Name, SafeName = @SafeName WHERE Id = @Id",
            new { Id = id, Name = name, SafeName = safeName });
    }

    public async Task UpdateApiKeyAsync(int id, string apiKey, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync(
            "UPDATE Users SET ApiKey = @ApiKey WHERE Id = @Id",
            new { Id = id, ApiKey = apiKey });
    }

    public async Task<User> CreateAsync(string name, string email, string pwBcrypt, string country,
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var id = await connection.ExecuteScalarAsync<int>(
            """
            INSERT INTO Users (Name, SafeName, Email, PwBcrypt, Country, CreationTime, LatestActivity)
            VALUES (@Name, @SafeName, @Email, @PwBcrypt, @Country, UNIX_TIMESTAMP(), UNIX_TIMESTAMP());
            SELECT LAST_INSERT_ID();
            """,
            new { Name = name, SafeName = SafeName.Make(name), Email = email, PwBcrypt = pwBcrypt, Country = country });

        return (await FetchByIdAsync(id, cancellationToken))!;
    }

    public async Task<IReadOnlyList<User>> FetchAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var rows = await connection.QueryAsync<UserRow>("SELECT * FROM Users ORDER BY Id");
        return rows.Select(r => r.ToUser()).ToList();
    }

    private MySqlConnection Connect()
    {
        return new MySqlConnection(connectionString);
    }

    // Mutable DTO so Dapper maps by property name (coercing column types loosely) instead of
    // strict positional-constructor-type matching.
    private sealed class UserRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string SafeName { get; set; } = "";
        public string? Email { get; set; }
        public int Priv { get; set; }
        public string Country { get; set; } = "";
        public int SilenceEnd { get; set; }
        public int DonorEnd { get; set; }
        public int CreationTime { get; set; }
        public int LatestActivity { get; set; }
        public int ClanId { get; set; }
        public int ClanPriv { get; set; }
        public int PreferredMode { get; set; }
        public int PlayStyle { get; set; }
        public string? CustomBadgeName { get; set; }
        public string? CustomBadgeIcon { get; set; }
        public string? UserpageContent { get; set; }
        public string? ApiKey { get; set; }

        public User ToUser()
        {
            return new User(
                Id, Name, SafeName, Email, Priv, Country, SilenceEnd, DonorEnd, CreationTime,
                LatestActivity, ClanId, ClanPriv, PreferredMode, PlayStyle, CustomBadgeName,
                CustomBadgeIcon, UserpageContent, ApiKey);
        }
    }
}