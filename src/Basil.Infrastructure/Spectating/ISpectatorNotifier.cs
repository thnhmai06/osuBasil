using Basil.Infrastructure.Shared.Sessions;

namespace Basil.Infrastructure.Spectating;

/// <summary>
///     Tells connected clients about changes to who is spectating whom. The spectator service
///     decides which player learns what; the implementation decides how that player's client
///     hears it.
/// </summary>
public interface ISpectatorNotifier
{
	/// <summary>Tells a host that a player has started spectating them.</summary>
	/// <param name="host">The spectated player.</param>
	/// <param name="spectatorId">The id of the player who started spectating.</param>
	void SpectatorJoined(GameSession host, int spectatorId);

	/// <summary>Tells a spectator that another player is spectating the same host.</summary>
	/// <param name="recipient">The spectator being told.</param>
	/// <param name="fellowId">The id of the fellow spectator.</param>
	void FellowSpectatorJoined(GameSession recipient, int fellowId);

	/// <summary>Tells a spectator that a fellow spectator has stopped spectating the same host.</summary>
	/// <param name="recipient">The spectator being told.</param>
	/// <param name="fellowId">The id of the spectator who left.</param>
	void FellowSpectatorLeft(GameSession recipient, int fellowId);
}
