namespace Basil.Application.Objects.Sessions;

/// <summary>Closes connections and keeps them alive.</summary>
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
	///     for 300 seconds. BasilBot's connection is not closed by this operation.
	/// </remarks>
	int CloseIdle();

	/// <summary>Brings BasilBot online with a connection of its own.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     The existing connection when BasilBot is already online, or <see langword="null" /> when its record is not
	///     stored or its connection could not be opened.
	/// </returns>
	/// <remarks>Used by the server at startup.</remarks>
	Task<BotConnection?> ConnectBotAsync(CancellationToken cancellationToken = default);
}
