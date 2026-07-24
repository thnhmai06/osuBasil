using Basil.Application.Abstractions.Users;
using Basil.Domain.Users;
using Microsoft.Extensions.Caching.Memory;

namespace Basil.Infrastructure.Caching;

/// <summary>
///     Read-through <see cref="IMemoryCache" /> decorator over the real <see cref="IUserRepository" />
///     — eliminates the N+1 that embedding a full <c>{id, name, country}</c> user reference into every
///     match/score response would otherwise cause. Every write invalidates the affected entry
///     immediately; the TTL is only a safety net bounding staleness/memory if an invalidation path is
///     ever missed, not a substitute for it.
/// </summary>
public sealed class CachingUserRepository(IUserRepository inner, IMemoryCache cache, TimeSpan? ttl = null)
    : IUserRepository
{
    private readonly TimeSpan _ttl = ttl ?? TimeSpan.FromMinutes(5);

    public async Task<User?> FetchByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var key = IdKey(id);
        if (cache.TryGetValue(key, out User? cached)) return cached;

        var user = await inner.FetchByIdAsync(id, cancellationToken);
        if (user is not null) cache.Set(key, user, _ttl);
        return user;
    }

    public async Task<User?> FetchByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var key = NameKey(name);
        if (cache.TryGetValue(key, out User? cached)) return cached;

        var user = await inner.FetchByNameAsync(name, cancellationToken);
        if (user is not null) cache.Set(key, user, _ttl);
        return user;
    }

    /// <summary>Never cached — a bcrypt hash has no business sitting in a general-purpose read cache.</summary>
    public Task<string?> FetchPasswordHashAsync(int id, CancellationToken cancellationToken = default)
    {
        return inner.FetchPasswordHashAsync(id, cancellationToken);
    }

    public async Task UpdateCountryAsync(int id, string country, CancellationToken cancellationToken = default)
    {
        await inner.UpdateCountryAsync(id, country, cancellationToken);
        cache.Remove(IdKey(id));
    }

    public async Task UpdatePrivilegesAsync(int id, UserPrivileges priv, CancellationToken cancellationToken = default)
    {
        await inner.UpdatePrivilegesAsync(id, priv, cancellationToken);
        cache.Remove(IdKey(id));
    }

    /// <summary>
    ///     Fetches the pre-rename row directly from <paramref name="inner" /> (bypassing the cache is
    ///     fine — a rename is rare) so the *old* name's cache entry can be invalidated too; otherwise a
    ///     lookup by the old (now-freed) name could keep resolving to this user until the TTL expires.
    /// </summary>
    public async Task UpdateNameAsync(int id, string name, string safeName, CancellationToken cancellationToken = default)
    {
        var before = await inner.FetchByIdAsync(id, cancellationToken);
        await inner.UpdateNameAsync(id, name, safeName, cancellationToken);
        cache.Remove(IdKey(id));
        if (before is not null) cache.Remove(NameKey(before.Name));
        cache.Remove(NameKey(name));
    }

    public Task<User?> CreateAsync(string name, string pwBcrypt, string country, UserPrivileges? priv = null,
        CancellationToken cancellationToken = default)
    {
        return inner.CreateAsync(name, pwBcrypt, country, priv, cancellationToken);
    }

    /// <summary>Uncached — a list-shaped admin route, not a hot single-row lookup.</summary>
    public Task<IReadOnlyList<User>> FetchAllAsync(CancellationToken cancellationToken = default)
    {
        return inner.FetchAllAsync(cancellationToken);
    }

    private static string IdKey(int id)
    {
        return $"User:Id:{id}";
    }

    private static string NameKey(string name)
    {
        return $"User:Name:{User.MakeSafeName(name)}";
    }
}
