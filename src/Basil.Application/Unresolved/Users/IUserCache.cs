using Basil.Application.Unresolved.Sessions;
using Basil.Domain.Users;

namespace Basil.Application.Unresolved.Users;

/// <summary>
///     Synchronous, best-effort lookup of an already-cached <see cref="User" /> by id, for code
///     paths that cannot await -- notably anything running under a match's lock.
/// </summary>
public interface IUserCache
{
	/// <summary>Gets the cached user for <paramref name="id" />, or <see langword="null" /> when not currently cached.</summary>
	/// <param name="id">The id of the user to look up.</param>
	User? TryGet(int id);
}

/// <summary>Convenience helpers built on <see cref="IUserCache" />.</summary>
public static class UserCacheExtensions
{
	/// <summary>
	///     Resolves the given session's <see cref="User" /> from the cache, falling back to a
	///     minimal placeholder built from the session's own id and name on a cache miss. A miss is
	///     expected to be rare: every session reaching this point has already completed login, which
	///     populates the cache.
	/// </summary>
	/// <param name="cache">The cache to resolve from.</param>
	/// <param name="session">The session whose user to resolve.</param>
	public static User Resolve(this IUserCache cache, UserSession session)
	{
		return cache.TryGet(session.Id) ?? new User { Id = session.Id, Name = session.Name };
	}

	/// <summary>
	///     Resolves a bare id (with an optional already-known name) from the cache, falling back to a
	///     minimal placeholder on a miss. See <see cref="Resolve(IUserCache,UserSession)" />.
	/// </summary>
	/// <param name="cache">The cache to resolve from.</param>
	/// <param name="id">The id of the user to resolve.</param>
	/// <param name="name">The user's name, if already known, used only on a cache miss.</param>
	public static User Resolve(this IUserCache cache, int id, string? name = null)
	{
		return cache.TryGet(id) ?? new User { Id = id, Name = name ?? $"user#{id}" };
	}
}