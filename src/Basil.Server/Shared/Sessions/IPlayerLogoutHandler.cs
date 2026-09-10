namespace Basil.Server.Shared.Sessions;

/// <summary>
///     Performs one slice-owned piece of the cleanup that runs when a session logs out.
/// </summary>
/// <remarks>
///     <see cref="PlayerLogoutService" /> runs every registered handler in ascending <see cref="Order" />,
///     regardless of which slice registered it. A handler decides for itself whether it applies to the
///     given session (for example, by pattern-matching on <see cref="GameSession" />) — there is no
///     separate contract for game versus IRC sessions. A handler that throws is logged and skipped so
///     the remaining handlers still run; see <see cref="PlayerLogoutService.LogoutAsync" />.
/// </remarks>
public interface IPlayerLogoutHandler
{
	/// <summary>
	///     Gets the position of this handler in the logout sequence, ascending. Lower values run first.
	/// </summary>
	int Order { get; }

	/// <summary>
	///     Performs this handler's share of the logout cleanup for <paramref name="session" />.
	/// </summary>
	/// <param name="session">The session that is logging out.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>A task that completes when this handler's cleanup has finished.</returns>
	Task OnLogoutAsync(UserSession session, CancellationToken cancellationToken);
}
