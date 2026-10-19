using Basil.Application.Storage.Contracts.Sessions;
using Basil.Domain.Users;

namespace Basil.Application.Services.Contracts.Sessions;

/// <summary>Closes connections and keeps them alive.</summary>
public interface ISessionConnectionsService
{
	/// <summary>What only osu! clients have: presence status and spectating.</summary>
	IBanchoConnectionService Bancho { get; }

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
	///     The connection asking: the user themselves, or a user with <see cref="Permissions.UserManage" /> who
	///     outranks them.
	/// </param>
	/// <param name="user">The user whose connections are closed.</param>
	/// <returns>
	///     <see langword="true" /> if the caller may close them, even when the user had none; otherwise,
	///     <see langword="false" />.
	/// </returns>
	/// <remarks>The connections, the caller's own included, are closed as <see cref="ConnectionCloseReason.Revoked" />.</remarks>
	bool Revoke(Connection by, UserRecord user);
}