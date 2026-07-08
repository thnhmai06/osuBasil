namespace OpenOsuTournament.Bancho.Application.Abstractions.Users;

/// <summary>Ported from app/repositories/users.py's User dataclass.</summary>
public sealed record User(
    int Id,
    string Name,
    string SafeName,
    string? Email,
    int Priv,
    string Country,
    int SilenceEnd,
    int DonorEnd,
    int CreationTime,
    int LatestActivity,
    int ClanId,
    int ClanPriv,
    int PreferredMode,
    int PlayStyle,
    string? CustomBadgeName,
    string? CustomBadgeIcon,
    string? UserpageContent,
    string? ApiKey);

/// <summary>
///     Ported from app/repositories/users.py's UsersRepository — scoped to what login (Phase 3)
///     needs. Broader filter/paging methods get added when a use case actually needs them.
/// </summary>
public interface IUserRepository
{
    Task<User?> FetchByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Looks up by SafeName.Make(name), matching bancho.py's safe_name lookup.</summary>
    Task<User?> FetchByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Fetches a user's bcrypt password hash. Intentionally separate from <see cref="User" /> so
    ///     the hash never rides along into general-purpose flows.
    /// </summary>
    Task<string?> FetchPasswordHashAsync(int id, CancellationToken cancellationToken = default);

    Task UpdateCountryAsync(int id, string country, CancellationToken cancellationToken = default);

    Task UpdatePrivilegesAsync(int id, int priv, CancellationToken cancellationToken = default);

    Task UpdateNameAsync(int id, string name, string safeName, CancellationToken cancellationToken = default);

    Task UpdateApiKeyAsync(int id, string apiKey, CancellationToken cancellationToken = default);

    Task<User> CreateAsync(string name, string email, string pwBcrypt, string country,
        CancellationToken cancellationToken = default);

    /// <summary>New for the management REST API's user listing.</summary>
    Task<IReadOnlyList<User>> FetchAllAsync(CancellationToken cancellationToken = default);
}