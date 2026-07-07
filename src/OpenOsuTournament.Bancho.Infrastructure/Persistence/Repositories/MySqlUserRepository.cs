using Dapper;
using MySqlConnector;
using OpenOsuTournament.Bancho.Application.Abstractions.Users;
using OpenOsuTournament.Bancho.Domain.Users;

namespace OpenOsuTournament.Bancho.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IUserRepository" />
public sealed class MySqlUserRepository(string connectionString) : IUserRepository
{
    // api_key is char(36) — MySqlConnector infers fixed-length char(36) columns as Guid, which
    // Dapper then fails to convert into the string-typed UserRow.ApiKey property. Cast explicitly
    // to force the driver to report it as a string.
    private const string SelectColumns = """
                                         id, name, safe_name AS SafeName, email, priv, country, silence_end AS SilenceEnd,
                                         donor_end AS DonorEnd, creation_time AS CreationTime, latest_activity AS LatestActivity,
                                         clan_id AS ClanId, clan_priv AS ClanPriv, preferred_mode AS PreferredMode,
                                         play_style AS PlayStyle, custom_badge_name AS CustomBadgeName,
                                         custom_badge_icon AS CustomBadgeIcon, userpage_content AS UserpageContent,
                                         CAST(api_key AS CHAR(36)) AS ApiKey
                                         """;

    // bancho.py's schema uses tinyint(1) for clan_priv (0-3, not a boolean) — MySqlConnector's
    // default TreatTinyAsBoolean=true would coerce any nonzero clan_priv value to 1. Disable it.
    private readonly string _connectionString = connectionString + ";TreatTinyAsBoolean=false";

    public async Task<User?> FetchByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var row = await connection.QuerySingleOrDefaultAsync<UserRow>(
            $"SELECT {SelectColumns} FROM users WHERE id = @Id",
            new { Id = id });
        return row?.ToUser();
    }

    public async Task<User?> FetchByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var row = await connection.QuerySingleOrDefaultAsync<UserRow>(
            $"SELECT {SelectColumns} FROM users WHERE safe_name = @SafeName",
            new { SafeName = SafeName.Make(name) });
        return row?.ToUser();
    }

    public async Task<string?> FetchPasswordHashAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        return await connection.QuerySingleOrDefaultAsync<string>(
            "SELECT pw_bcrypt FROM users WHERE id = @Id",
            new { Id = id });
    }

    public async Task UpdateCountryAsync(int id, string country, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync(
            "UPDATE users SET country = @Country WHERE id = @Id",
            new { Id = id, Country = country });
    }

    public async Task UpdatePrivilegesAsync(int id, int priv, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync(
            "UPDATE users SET priv = @Priv WHERE id = @Id",
            new { Id = id, Priv = priv });
    }

    public async Task UpdateNameAsync(int id, string name, string safeName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync(
            "UPDATE users SET name = @Name, safe_name = @SafeName WHERE id = @Id",
            new { Id = id, Name = name, SafeName = safeName });
    }

    public async Task UpdateApiKeyAsync(int id, string apiKey, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync(
            "UPDATE users SET api_key = @ApiKey WHERE id = @Id",
            new { Id = id, ApiKey = apiKey });
    }

    public async Task<User> CreateAsync(string name, string email, string pwBcrypt, string country,
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var id = await connection.ExecuteScalarAsync<int>(
            """
            INSERT INTO users (name, safe_name, email, pw_bcrypt, country, creation_time, latest_activity)
            VALUES (@Name, @SafeName, @Email, @PwBcrypt, @Country, UNIX_TIMESTAMP(), UNIX_TIMESTAMP());
            SELECT LAST_INSERT_ID();
            """,
            new { Name = name, SafeName = SafeName.Make(name), Email = email, PwBcrypt = pwBcrypt, Country = country });

        return (await FetchByIdAsync(id, cancellationToken))!;
    }

    private MySqlConnection Connect()
    {
        return new MySqlConnection(_connectionString);
    }

    // Mutable DTO so Dapper maps by property name (coercing column types loosely) instead of
    // strict positional-constructor-type matching, which broke on bancho.py's tinyint(1)/char(36)
    // columns getting reported as SByte/Guid by MySqlConnector's driver-level type inference.
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