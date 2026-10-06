using Basil.Application.Services.Contracts.Events;
using Basil.Application.Storage.Contracts.Sessions;
using Basil.Domain.Users;

namespace Basil.Application.Services.Contracts.Sessions;

/// <summary>Opens and closes the connections of online users and changes what they share.</summary>
public interface ISessionService : IEventPublisher<UserEvent>
{
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

	/// <summary>Closes every connection that has sent nothing for too long.</summary>
	/// <returns>How many connections were closed.</returns>
	/// <remarks>
	///     Run periodically by the host. A connection of the HTTP API may stay idle for 2 hours; any other connection
	///     for 300 seconds.
	/// </remarks>
	int CloseIdle();

	/// <summary>Shows a notification to online osu! clients.</summary>
	/// <param name="by">The connection asking; its user needs <see cref="Permissions.ModeratorAnnounce" />.</param>
	/// <param name="text">The text to show.</param>
	/// <param name="to">The users to notify, or <see langword="null" /> for every online user.</param>
	/// <returns>
	///     How many clients were sent the notification, or <see langword="null" /> when the caller may not announce.
	/// </returns>
	int? Announce(Connection by, string text, IReadOnlyCollection<User>? to = null);

	/// <summary>Closes every connection of a user.</summary>
	/// <param name="by">
	///     The connection asking: the user themselves, or a user with <see cref="Permissions.OwnerManageAccounts" /> who
	///     outranks them.
	/// </param>
	/// <param name="user">The user whose connections are closed.</param>
	/// <returns>
	///     <see langword="true" /> if the caller may close them, even when the user had none; otherwise,
	///     <see langword="false" />.
	/// </returns>
	/// <remarks>The connections, the caller's own included, are closed as <see cref="ConnectionCloseReason.Revoked" />.</remarks>
	bool Revoke(Connection by, User user);

	/// <summary>Finds the connection through which an account may act for another online user.</summary>
	/// <param name="by">The connection asking; its user needs <see cref="Permissions.TournamentActForUsers" />.</param>
	/// <param name="user">The user to act for.</param>
	/// <returns>A connection that acts for the user, or why there is none.</returns>
	/// <remarks>
	///     An operation run through the returned connection is checked against, and attributed to, that user, but only
	///     the user's permissions in <see cref="Permissions.RoomDelegation" /> count: room and lobby operations work,
	///     anything else is refused. The caller's rank does not matter, since the user's own authority applies. The
	///     connection takes no part in channels.
	/// </remarks>
	(DelegatedConnection? Connection, DelegationFailure? Failure) ActFor(Connection by, User user);
}

/// <summary>The reasons an account cannot act for another user.</summary>
public enum DelegationFailure : byte
{
	/// <summary>The caller may not act for other users.</summary>
	NotPermitted,

	/// <summary>The user to act for has no open connection.</summary>
	UserOffline
}