namespace Basil.Server.Shared.Sessions;

/// <summary>
///     Removes a departing <see cref="GameSession" /> from the live session registry.
/// </summary>
public sealed class GameSessionRegistryRemovalLogoutHandler(ISessionRegistry<GameSession> gameRegistry)
	: IPlayerLogoutHandler
{
	/// <inheritdoc />
	public int Order => 40;

	/// <inheritdoc />
	public Task OnLogoutAsync(UserSession session, CancellationToken cancellationToken)
	{
		if (session is GameSession game) gameRegistry.Remove(game);

		return Task.CompletedTask;
	}
}