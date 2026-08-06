namespace Basil.Application.Sessions;

/// <summary>
///     Represents the in-memory registry of currently online sessions, maintained as sessions are
///     added at login and removed at logout. An account may hold at most one <see cref="GameSession" />
///     and at most one <see cref="IrcSession" /> at a time, registered and looked up independently.
/// </summary>
public interface IUserSessionRegistry
{
	/// <summary>Gets a snapshot of every currently registered session, of either kind.</summary>
	IReadOnlyCollection<UserSession> All { get; }

	/// <summary>Gets a snapshot of every currently registered <see cref="GameSession" />.</summary>
	IReadOnlyCollection<GameSession> GameSessions { get; }

	/// <summary>Gets a snapshot of every currently registered <see cref="IrcSession" />.</summary>
	IReadOnlyCollection<IrcSession> IrcSessions { get; }

	/// <summary>
	///     Registers <paramref name="session" /> if the account does not already hold a
	///     <see cref="GameSession" />.
	/// </summary>
	/// <param name="session">The game session to register.</param>
	/// <returns>
	///     <see langword="true" /> if the session was registered; <see langword="false" /> if the
	///     account already has a live <see cref="GameSession" />. This is the final authority on
	///     whether the account is already logged in — any prior lookup used only to evict a stale
	///     session is advisory, not a guarantee.
	/// </returns>
	bool TryAddGameSession(GameSession session);

	/// <summary>
	///     Registers <paramref name="session" /> if the account does not already hold an
	///     <see cref="IrcSession" />.
	/// </summary>
	/// <param name="session">The IRC session to register.</param>
	/// <returns>
	///     <see langword="true" /> if the session was registered; <see langword="false" /> if the
	///     account already has a live <see cref="IrcSession" />.
	/// </returns>
	bool TryAddIrcSession(IrcSession session);

	/// <summary>
	///     Removes <paramref name="session" /> from the registry, marking it offline. A no-op if the
	///     registry no longer holds that exact session instance (for example, a stale cleanup racing
	///     a session that already replaced it).
	/// </summary>
	/// <param name="session">The session being logged out.</param>
	void Remove(UserSession session);

	/// <summary>Gets the <see cref="GameSession" /> whose login token matches <paramref name="token" />, or null.</summary>
	/// <param name="token">The login token a client used to authenticate.</param>
	GameSession? GetGameByToken(string token);

	/// <summary>Gets the online <see cref="GameSession" /> for the userSession with id <paramref name="userId" />, or null.</summary>
	/// <param name="userId">The persistent id of the userSession.</param>
	GameSession? GetGameByUserId(int userId);

	/// <summary>Gets the online <see cref="GameSession" /> whose safe name matches <paramref name="name" />, or null.</summary>
	/// <param name="name">The name of the userSession to look up.</param>
	GameSession? GetGameByName(string name);

	/// <summary>Gets the online <see cref="IrcSession" /> for the userSession with id <paramref name="userId" />, or null.</summary>
	/// <param name="userId">The persistent id of the userSession.</param>
	IrcSession? GetIrcByUserId(int userId);

	/// <summary>Gets the online <see cref="IrcSession" /> whose safe name matches <paramref name="name" />, or null.</summary>
	/// <param name="name">The name of the userSession to look up.</param>
	IrcSession? GetIrcByName(string name);

	/// <summary>
	///     Gets every session currently registered for the userSession with id <paramref name="userId" /> —
	///     0, 1, or 2 entries (a <see cref="GameSession" />, an <see cref="IrcSession" />, or both).
	/// </summary>
	/// <param name="userId">The persistent id of the userSession.</param>
	IReadOnlyList<UserSession> GetSessionsByUserId(int userId);
}
