using Basil.Application.Sessions;

namespace Basil.Application.Content;

/// <summary>
///     Tells a connected client about an admin-pushed announcement. The announce endpoint decides
///     who hears it; the implementation decides how that player's client hears it.
/// </summary>
public interface IAnnouncementNotifier
{
	/// <summary>Delivers an announcement to one online session.</summary>
	/// <param name="session">The session to notify.</param>
	/// <param name="text">The announcement text.</param>
	void Announce(GameSession session, string text);
}
