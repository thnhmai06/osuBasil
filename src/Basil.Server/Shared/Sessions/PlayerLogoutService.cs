namespace Basil.Server.Shared.Sessions;

/// <summary>
///     Performs the coordinated cleanup that runs when a session goes offline. Shared by the LOGOUT
///     packet handler, the <c>!reconnect</c> command, and a real IRC connection's disconnect, all of
///     which force the same cleanup on a session outside the normal logout flow.
/// </summary>
/// <remarks>
///     The actual cleanup is a list of <see cref="IPlayerLogoutHandler" /> instances, one per
///     slice-owned concern, run in ascending <see cref="IPlayerLogoutHandler.Order" />. A handler that
///     throws is logged and skipped rather than aborting the sequence, so one failing step cannot leave
///     the rest of the teardown undone — a partially cleaned-up session is exactly the ghost
///     <see cref="GhostDisconnectService" /> exists to mop up later. Cancelling the logout itself (an
///     <see cref="OperationCanceledException" />) is not treated as a handler failure and propagates
///     instead, aborting the remaining handlers.
/// </remarks>
public sealed class PlayerLogoutService(
	IEnumerable<IPlayerLogoutHandler> handlers,
	ILogger<PlayerLogoutService> logger)
{
	private readonly IReadOnlyList<IPlayerLogoutHandler> _orderedHandlers =
		handlers.OrderBy(handler => handler.Order).ToArray();

	/// <summary>
	///     Logs <paramref name="userSession" /> out, running every registered logout handler in order.
	/// </summary>
	/// <param name="userSession">The session being logged out.</param>
	/// <param name="cancellationToken">A token that cancels the remaining handlers.</param>
	/// <returns>A task that completes when the logout cleanup has finished.</returns>
	public async Task LogoutAsync(UserSession userSession, CancellationToken cancellationToken = default)
	{
		logger.LogInformation(
			"- User logged out: UserId={UserId} Username={Username}", userSession.Id, userSession.Name);

		foreach (var handler in _orderedHandlers)
			try
			{
				await handler.OnLogoutAsync(userSession, cancellationToken);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				logger.LogError(ex,
					"Logout handler {Handler} failed: UserId={UserId} Username={Username}",
					handler.GetType().Name, userSession.Id, userSession.Name);
			}
	}
}