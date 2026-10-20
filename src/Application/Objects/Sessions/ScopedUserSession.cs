namespace Basil.Application.Objects.Sessions;

/// <summary>A user session scoped to one connection.</summary>
public sealed class ScopedUserSession
{
	/// <summary>The session this scope is over.</summary>
	public UserSession Session { get; }

	/// <summary>The connection acting on this session.</summary>
	public Connection By { get; }

	/// <summary>Creates a session scope for a connection.</summary>
	/// <param name="session">The session to scope.</param>
	/// <param name="by">The connection acting on the session.</param>
	internal ScopedUserSession(UserSession session, Connection by)
	{
		Session = session;
		By = by;
	}

	/// <summary>Sets or clears the away message other users see.</summary>
	/// <param name="message">The away message, or <see langword="null" /> or white space to clear it.</param>
	/// <returns><see langword="true" /> if this connection belongs to this session and the message was set.</returns>
	public bool SetAway(string? message)
	{
		if (!ReferenceEquals(By.Session, Session)) return false;

		Session.AwayMessage = string.IsNullOrWhiteSpace(message) ? null : message;
		return true;
	}

	/// <summary>Sets whether this user accepts private messages only from their friends.</summary>
	/// <param name="pmPrivate"><see langword="true" /> to accept private messages only from friends.</param>
	/// <returns><see langword="true" /> if this connection belongs to this session and the setting was set.</returns>
	public bool SetPmPrivate(bool pmPrivate)
	{
		if (!ReferenceEquals(By.Session, Session)) return false;

		Session.PmPrivate = pmPrivate;
		return true;
	}
}