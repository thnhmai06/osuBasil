namespace Basil.Application.Sessions;

/// <summary>
///     Represents the in-memory registry of currently online players, maintained as sessions are
///     added at login and removed at logout. Login, packet dispatch, spectator, and multiplayer
///     membership logic all consult it to reach the session behind a token, id, or name.
/// </summary>
public interface IUserSessionRegistry
{
	/// <summary>Gets a snapshot of every currently registered userSession session.</summary>
	IReadOnlyCollection<UserSession> All { get; }

	/// <summary>
	///     Adds <paramref name="session" /> to the registry so the userSession becomes discoverable to
	///     the rest of the server.
	/// </summary>
	/// <param name="session">The session of the userSession who just logged in.</param>
	void Add(UserSession session);

	/// <summary>
	///     Removes <paramref name="session" /> from the registry, marking the userSession as offline.
	/// </summary>
	/// <param name="session">The session of the userSession being logged out.</param>
	void Remove(UserSession session);

	/// <summary>
	///     Gets the session whose login token matches <paramref name="token" />, or null if no
	///     online userSession authenticated with that token.
	/// </summary>
	/// <param name="token">The login token a client used to authenticate.</param>
	/// <returns>The matching session, or null if the token is unknown.</returns>
	UserSession? GetByToken(string token);

	/// <summary>
	///     Gets the session of the online userSession with id <paramref name="id" />, or null if that
	///     userSession is not currently online.
	/// </summary>
	/// <param name="id">The persistent id of the userSession.</param>
	/// <returns>The userSession's session, or null if the userSession is offline.</returns>
	UserSession? GetById(int id);

	/// <summary>
	///     Gets the session whose safe name equals the safe form of <paramref name="name" />, as
	///     produced by <see cref="Basil.Domain.Users.User.MakeSafeName" />, or null if no online
	///     userSession matches.
	/// </summary>
	/// <param name="name">The name of the userSession to look up.</param>
	/// <returns>The matching session, or null if no online userSession matches.</returns>
	UserSession? GetByName(string name);
}