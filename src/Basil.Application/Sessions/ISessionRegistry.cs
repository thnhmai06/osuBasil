namespace Basil.Application.Sessions;

/// <summary>
///     Represents the in-memory registry of currently online sessions of a single kind,
///     maintained as sessions are added at login and removed at logout. An account may hold at most
///     one session of the kind at a time, so each registry resolves at most one session per UserId,
///     LoginToken, or name.
/// </summary>
/// <typeparam name="TSession">The concrete session type this registry holds</typeparam>
public interface ISessionRegistry<TSession>
	where TSession : UserSession
{
	/// <summary>Gets a snapshot of every currently registered session.</summary>
	IEnumerable<TSession> All { get; }

	/// <summary>
	///     Registers <paramref name="session" /> if the account does not already hold a session of
	///     this kind.
	/// </summary>
	/// <param name="session">The session to register.</param>
	/// <returns>
	///     <see langword="true" /> if the session was registered; <see langword="false" /> if the
	///     account already has a live session of this kind. This is the final authority on whether
	///     the account is already logged in — any prior lookup used only to evict a stale session is
	///     advisory, not a guarantee.
	/// </returns>
	bool TryAdd(TSession session);

	/// <summary>
	///     Removes <paramref name="session" /> from the registry, marking it offline. A no-op if the
	///     registry no longer holds that exact session instance (for example, a stale cleanup racing
	///     a session that already replaced it).
	/// </summary>
	/// <param name="session">The session being logged out.</param>
	void Remove(TSession session);

	/// <summary>Gets the session whose login token matches <paramref name="token" />, or null.</summary>
	/// <param name="token">The login token a client used to authenticate.</param>
	TSession? GetByToken(string token);

	/// <summary>Gets the online session for the userSession with id <paramref name="userId" />, or null.</summary>
	/// <param name="userId">The persistent id of the userSession.</param>
	TSession? GetByUserId(int userId);

	/// <summary>Gets the online session whose safe name matches <paramref name="name" />, or null.</summary>
	/// <param name="name">The name of the userSession to look up.</param>
	TSession? GetByName(string name);
}