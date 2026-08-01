namespace Basil.Application.Sessions;

/// <summary>
///     Represents the in-memory registry of currently online players, maintained as sessions are
///     added at login and removed at logout. Login, packet dispatch, spectator, and multiplayer
///     membership logic all consult it to reach the session behind a token, id, or name.
/// </summary>
public interface IPlayerSessionRegistry
{
	/// <summary>Gets a snapshot of every currently registered player session.</summary>
	IReadOnlyList<PlayerSession> All { get; }

	/// <summary>
	///     Adds <paramref name="session" /> to the registry so the player becomes discoverable to
	///     the rest of the server.
	/// </summary>
	/// <param name="session">The session of the player who just logged in.</param>
	void Add(PlayerSession session);

	/// <summary>
	///     Removes <paramref name="session" /> from the registry, marking the player as offline.
	/// </summary>
	/// <param name="session">The session of the player being logged out.</param>
	void Remove(PlayerSession session);

	/// <summary>
	///     Gets the session whose login token matches <paramref name="token" />, or null if no
	///     online player authenticated with that token.
	/// </summary>
	/// <param name="token">The login token a client used to authenticate.</param>
	/// <returns>The matching session, or null if the token is unknown.</returns>
	PlayerSession? GetByToken(string token);

	/// <summary>
	///     Gets the session of the online player with id <paramref name="id" />, or null if that
	///     player is not currently online.
	/// </summary>
	/// <param name="id">The persistent id of the player.</param>
	/// <returns>The player's session, or null if the player is offline.</returns>
	PlayerSession? GetById(int id);

	/// <summary>
	///     Gets the session whose safe name equals the safe form of <paramref name="name" />, as
	///     produced by <see cref="Basil.Domain.Users.User.MakeSafeName" />, or null if no online
	///     player matches.
	/// </summary>
	/// <param name="name">The name of the player to look up.</param>
	/// <returns>The matching session, or null if no online player matches.</returns>
	PlayerSession? GetByName(string name);
}