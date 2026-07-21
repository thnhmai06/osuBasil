using Basil.Domain.Users;

namespace Basil.Application.Abstractions.Users;

/// <summary>
///     Ported from app/repositories/users.py's UsersRepository — scoped to what login needs.
///     Broader filter/paging methods are added when a use case needs them.
/// </summary>
public interface IUserRepository
{
    Task<User?> FetchByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Looks up by User.MakeSafeName(name), matching bancho.py's safe_name lookup.</summary>
    Task<User?> FetchByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Fetches a user's bcrypt password hash. Intentionally separate from <see cref="User" /> so
    ///     the hash never rides along into general-purpose flows.
    /// </summary>
    Task<string?> FetchPasswordHashAsync(int id, CancellationToken cancellationToken = default);

    Task UpdateCountryAsync(int id, string country, CancellationToken cancellationToken = default);

    Task UpdatePrivilegesAsync(int id, UserPrivileges priv, CancellationToken cancellationToken = default);

    Task UpdateNameAsync(int id, string name, string safeName, CancellationToken cancellationToken = default);

    /// <summary>Null when Name/SafeName collides with an existing row (a concurrent registration won the race).</summary>
    Task<User?> CreateAsync(string name, string pwBcrypt, string country, UserPrivileges? priv = null,
        CancellationToken cancellationToken = default);

    /// <summary>For the management REST API's user listing.</summary>
    Task<IReadOnlyList<User>> FetchAllAsync(CancellationToken cancellationToken = default);
}