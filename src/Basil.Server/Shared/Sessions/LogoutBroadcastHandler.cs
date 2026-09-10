using Basil.Protocol.Packets;

namespace Basil.Server.Shared.Sessions;

/// <summary>
///     Broadcasts a departing, unrestricted <see cref="GameSession" />'s logout to every other online
///     game session, since it is the only kind osu! clients ever saw as an online "player" in the
///     first place.
/// </summary>
public sealed class LogoutBroadcastHandler(ISessionRegistry<GameSession> gameRegistry) : IPlayerLogoutHandler
{
	/// <inheritdoc />
	public int Order => 60;

	/// <inheritdoc />
	public Task OnLogoutAsync(UserSession session, CancellationToken cancellationToken)
	{
		if (session is GameSession { Restricted: false } game)
			foreach (var other in gameRegistry.All)
				other.Enqueue(ServerPacketWriter.Logout(game.Id));

		return Task.CompletedTask;
	}
}
