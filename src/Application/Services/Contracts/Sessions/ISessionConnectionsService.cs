using Basil.Application.Storage.Contracts.Sessions;
using Basil.Domain.Users;

namespace Basil.Application.Services.Contracts.Sessions;

/// <summary>Closes connections, keeps them alive and lets an account act for another user.</summary>
public interface ISessionConnectionsService
{
	/// <summary>Closes a connection, taking its user offline if it was their last one.</summary>
	/// <param name="connection">The connection to close.</param>
	/// <param name="reason">Why the connection is closed.</param>
	/// <remarks>
	///     An osu! client's logout within one second of login is ignored: the osu! client sends one right after logging in.
	///     Closing a connection that is already closed does nothing.
	/// </remarks>
	void Close(Connection connection, ConnectionCloseReason reason);

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
