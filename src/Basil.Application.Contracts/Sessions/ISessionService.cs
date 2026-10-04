using Basil.Application.Contracts.Events;
using Basil.Application.Storage.Sessions;

namespace Basil.Application.Contracts.Sessions;

/// <summary>Opens and closes the connections of online users and changes what they share.</summary>
public interface ISessionService : IEventPublisher<UserEvent>
{
	/// <summary>Brings BasilBot online.</summary>
	/// <returns>BasilBot's connection.</returns>
	/// <remarks>
	///     BasilBot is the user with id <see cref="Basil.Domain.Users.SystemUserIds.BasilBot" />; it is created with its
	///     default data when it does not exist. Calling it again while BasilBot is online returns the open connection.
	/// </remarks>
	Task<BotConnection> OpenBotAsync(CancellationToken cancellationToken = default);

	/// <summary>Closes a connection, taking its user offline if it was their last one.</summary>
	/// <param name="connection">The connection to close.</param>
	/// <param name="reason">Why the connection is closed.</param>
	/// <remarks>
	///     An osu! client's logout within one second of login is ignored: the osu! client sends one right after logging in.
	///     Closing a connection that is already closed does nothing.
	/// </remarks>
	void Close(Connection connection, ConnectionCloseReason reason);

	/// <summary>Records the presence status an osu! client reported.</summary>
	/// <param name="by">The client connection that reported it.</param>
	/// <param name="status">The reported status.</param>
	/// <remarks>Reporting the status the client already has, or reporting from a closed connection, does nothing.</remarks>
	void SetStatus(BanchoConnection by, PlayerStatus status);

	/// <summary>Sets or clears the away message other users see.</summary>
	/// <param name="session">The user's session.</param>
	/// <param name="message">The away message, or <see langword="null" /> or white space to clear it.</param>
	void SetAway(UserSession session, string? message);

	/// <summary>Sets whether a user accepts private messages only from their friends.</summary>
	/// <param name="session">The user's session.</param>
	/// <param name="pmPrivate"><see langword="true" /> to accept private messages only from friends.</param>
	void SetPmPrivate(UserSession session, bool pmPrivate);

	/// <summary>Records that a client just sent something.</summary>
	/// <param name="connection">The connection that sent a message.</param>
	/// <remarks>A connection that sends nothing for too long is closed by <see cref="CloseIdle" />.</remarks>
	void MarkActive(Connection connection);

	/// <summary>Closes every client connection that has sent nothing for too long.</summary>
	/// <returns>How many connections were closed.</returns>
	/// <remarks>Run periodically by the host. BasilBot's connection is never closed.</remarks>
	int CloseIdle();

	/// <summary>Shows a notification to online osu! clients.</summary>
	/// <param name="text">The text to show.</param>
	/// <param name="to">The users to notify, or <see langword="null" /> for every online user.</param>
	/// <returns>How many clients were sent the notification.</returns>
	int Announce(string text, IReadOnlyCollection<User>? to = null);
}