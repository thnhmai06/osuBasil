using Basil.Server.Shared.Sessions;

namespace Basil.Server.Features.Chat;

/// <summary>
///     Delivers chat to a connected player. The caller decides who hears what; the implementation
///     decides how that player's transport carries it.
/// </summary>
public interface IChatNotifier
{
	/// <summary>Delivers one line of chat to a player, whatever transport they are connected on.</summary>
	/// <remarks>Never blocks on I/O: chat is delivered from inside match-state transitions.</remarks>
	/// <param name="recipient">The player who receives the line.</param>
	/// <param name="line">The line to deliver.</param>
	void Deliver(UserSession recipient, ChatLine line);
}
