using Basil.Application.Abstractions.Users;
using Basil.Domain.Login;
using Basil.Domain.Users;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Cache;

/// <summary>
///     Read-through caching decorator over the real <see cref="IUserRepository" />: eliminates the
///     N+1 that embedding a full <c>{id, name, country}</c> user reference into every response that
///     carries one would otherwise cause. Every write invalidates the affected entry immediately;
///     the TTL is only a safety net bounding staleness or memory use if an invalidation path is
///     ever missed, not a substitute for it.
/// </summary>
public sealed class CachingUserRepository(
	IUserRepository inner,
	IMemoryCache cache,
	ILogger<CachingUserRepository> logger,
	TimeSpan? ttl = null)
	: IUserRepository
{
	/// <summary>The entry TTL: the safety net beneath explicit invalidation.</summary>
	private readonly TimeSpan _ttl = ttl ?? TimeSpan.FromMinutes(5);

	/// <inheritdoc cref="IUserRepository.FetchByIdAsync" />
	/// <remarks>
	///     Read-through: a hit returns immediately, a miss fetches from the underlying repository, and
	///     caches the non-null result.
	/// </remarks>
	public async Task<User?> FetchByIdAsync(int id, CancellationToken cancellationToken = default)
	{
		var key = IdKey(id);
		if (cache.TryGetValue(key, out User? cached))
		{
			logger.LogDebug("Cache hit {Key}", key);
			return cached;
		}

		logger.LogDebug("Cache miss {Key}", key);
		var user = await inner.FetchByIdAsync(id, cancellationToken);
		if (user is not null)
			cache.Set(key, user, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = _ttl, Size = 1 });
		return user;
	}

	/// <inheritdoc cref="IUserRepository.FetchByNameAsync" />
	/// <remarks>
	///     Read-through, keyed by the safe form of the name (see <see cref="User.MakeSafeName" />) so a
	///     lookup matches regardless of case or spaces.
	/// </remarks>
	public async Task<User?> FetchByNameAsync(string name, CancellationToken cancellationToken = default)
	{
		var key = NameKey(name);
		if (cache.TryGetValue(key, out User? cached))
		{
			logger.LogDebug("Cache hit {Key}", key);
			return cached;
		}

		logger.LogDebug("Cache miss {Key}", key);
		var user = await inner.FetchByNameAsync(name, cancellationToken);
		if (user is not null)
			cache.Set(key, user, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = _ttl, Size = 1 });
		return user;
	}

	/// <summary>Never cached: a bcrypt hash has no business sitting in a general-purpose read cache.</summary>
	public Task<string?> FetchPasswordHashAsync(int id, CancellationToken cancellationToken = default)
	{
		return inner.FetchPasswordHashAsync(id, cancellationToken);
	}

	/// <inheritdoc cref="IUserRepository.UpdateCountryAsync" />
	/// <remarks>Invalidates the id-keyed entry after updating.</remarks>
	public async Task UpdateCountryAsync(int id, Country country, CancellationToken cancellationToken = default)
	{
		await inner.UpdateCountryAsync(id, country, cancellationToken);
		cache.Remove(IdKey(id));
	}

	/// <inheritdoc cref="IUserRepository.UpdatePrivilegesAsync" />
	/// <remarks>Invalidates the id-keyed entry after updating.</remarks>
	public async Task UpdatePrivilegesAsync(int id, UserPrivileges privilege,
		CancellationToken cancellationToken = default)
	{
		await inner.UpdatePrivilegesAsync(id, privilege, cancellationToken);
		cache.Remove(IdKey(id));
	}

	/// <inheritdoc cref="IUserRepository.UpdateNameAsync" />
	/// <remarks>
	///     Invalidates the old-name entry as well as the new-name and id entries, so a lookup by the
	///     previous name stops resolving to this user.
	/// </remarks>
	public async Task UpdateNameAsync(int id, string name, string safeName,
		CancellationToken cancellationToken = default)
	{
		var before = await inner.FetchByIdAsync(id, cancellationToken);
		await inner.UpdateNameAsync(id, name, safeName, cancellationToken);
		cache.Remove(IdKey(id));
		if (before is not null) cache.Remove(NameKey(before.Name));
		cache.Remove(NameKey(name));
	}

	/// <inheritdoc cref="IUserRepository.SoftDeleteAsync" />
	/// <remarks>
	///     Invalidates both the id- and name-keyed entries after updating -- unlike the other update
	///     methods here, a stale name-keyed hit would let a deleted account's login check
	///     (<see cref="FetchByNameAsync" />) read a cached pre-deletion row and let them log back in.
	/// </remarks>
	public async Task SoftDeleteAsync(int id, DateTimeOffset deletedAt, CancellationToken cancellationToken = default)
	{
		var before = await inner.FetchByIdAsync(id, cancellationToken);
		await inner.SoftDeleteAsync(id, deletedAt, cancellationToken);
		cache.Remove(IdKey(id));
		if (before is not null) cache.Remove(NameKey(before.Name));
	}

	/// <inheritdoc cref="IUserRepository.CreateAsync" />
	/// <remarks>Passes straight through: a brand-new user cannot already be cached.</remarks>
	public Task<User?> CreateAsync(string name, string pwBcrypt, Country country, UserPrivileges? privilege = null,
		CancellationToken cancellationToken = default)
	{
		return inner.CreateAsync(name, pwBcrypt, country, privilege, cancellationToken);
	}

	/// <summary>Uncached: a list-shaped call, not a single-row lookup.</summary>
	public Task<IReadOnlyList<User>> FetchAllAsync(CancellationToken cancellationToken = default)
	{
		return inner.FetchAllAsync(cancellationToken);
	}

	/// <summary>Builds the id-qualified cache key for a user.</summary>
	private static string IdKey(int id)
	{
		return $"User:Id:{id}";
	}

	/// <summary>
	///     Builds the name-qualified cache key for a user, normalized via <see cref="User.MakeSafeName" />.
	/// </summary>
	private static string NameKey(string name)
	{
		return $"User:Name:{User.MakeSafeName(name)}";
	}
}