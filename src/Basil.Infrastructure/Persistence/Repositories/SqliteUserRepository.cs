using Basil.Application.Abstractions.Users;
using Basil.Domain.Users;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IUserRepository" />
public sealed class SqliteUserRepository(string connectionString) : IUserRepository
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
            new { SafeName = User.MakeSafeName(name) });
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

    public async Task UpdatePrivilegesAsync(int id, UserPrivileges priv, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync(
            "UPDATE Users SET Priv = @Priv WHERE Id = @Id",
            new { Id = id, Priv = (int)priv });
    }

    public async Task UpdateNameAsync(int id, string name, string safeName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync(
            "UPDATE Users SET Name = @Name, SafeName = @SafeName WHERE Id = @Id",
            new { Id = id, Name = name, SafeName = safeName });
    }

    public async Task<User?> CreateAsync(string name, string pwBcrypt, string country, UserPrivileges? priv = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        int id;
        try
        {
            id = await connection.ExecuteScalarAsync<int>(
                """
                INSERT INTO Users (Name, SafeName, PwBcrypt, Country, Priv)
                VALUES (@Name, @SafeName, @PwBcrypt, @Country, @Priv);
                SELECT last_insert_rowid();
                """,
                new
                {
                    Name = name,
                    SafeName = User.MakeSafeName(name),
                    PwBcrypt = pwBcrypt,
                    Country = country,
                    Priv = (int)(priv ?? (UserPrivileges.Unrestricted | UserPrivileges.Verified | UserPrivileges.Supporter))
                });
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // SQLITE_CONSTRAINT (Name/SafeName UNIQUE)
        {
            return null;
        }

        return (await FetchByIdAsync(id, cancellationToken))!;
    }

    public async Task<IReadOnlyList<User>> FetchAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var rows = await connection.QueryAsync<UserRow>("SELECT * FROM Users ORDER BY Id");
        return rows.Select(r => r.ToUser()).ToList();
    }

    private SqliteConnection Connect()
    {
        return new SqliteConnection(connectionString);
    }

    // Mutable DTO so Dapper maps by property name (coercing column types loosely) instead of
    // strict positional-constructor-type matching.
    private sealed class UserRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string SafeName { get; set; } = "";
        public int Priv { get; set; }
        public string Country { get; set; } = "";
        public DateTime SilenceEnd { get; set; }

        public User ToUser()
        {
            var country = Enum.TryParse<Basil.Domain.Login.Country>(Country, ignoreCase: true, out var parsed)
                ? parsed
                : Basil.Domain.Login.Country.Xx;
            return new User(Id, Name, country, (UserPrivileges)Priv,
                new DateTimeOffset(DateTime.SpecifyKind(SilenceEnd, DateTimeKind.Utc)));
        }
    }
}
