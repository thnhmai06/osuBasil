using Basil.Domain.Users;
using Basil.Server.Shared.Sessions;

namespace Basil.Server.Features.Spectating;

/// <summary>
///     Tears down the spectator relationships of a departing <see cref="GameSession" />: the session
///     it was spectating, and BasilBot's own watch of the session, which would otherwise be left with
///     a dead member reference until this same user logs back in.
/// </summary>
public sealed class SpectatorTeardownLogoutHandler(
	ISessionRegistry<GameSession> gameRegistry,
	SpectatorService spectatorService) : IPlayerLogoutHandler
{
	/// <inheritdoc />
	public int Order => 20;

	/// <inheritdoc />
	public Task OnLogoutAsync(UserSession session, CancellationToken cancellationToken)
	{
		if (session is not GameSession game) return Task.CompletedTask;

		if (game.Spectating is { } host) spectatorService.RemoveSpectator(host, game);

		// #spec_{userId} is keyed by the persistent user id, stable across relogins -- tear down
		// BasilBot's own watch of this departing session now, or the channel would be left with a
		// dead member reference until this same user logs back in and re-triggers AddSpectator.
		var bot = gameRegistry.GetByUserId(SystemUserIds.BasilBot);
		if (bot is not null) spectatorService.RemoveSpectator(game, bot);

		return Task.CompletedTask;
	}
}
