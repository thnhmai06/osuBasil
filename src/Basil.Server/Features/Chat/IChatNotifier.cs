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

	/// <summary>Tells a player that a private message they sent was not delivered.</summary>
	/// <param name="sender">The player whose message was refused.</param>
	/// <param name="recipientName">The name of the player the message was addressed to.</param>
	/// <param name="reason">Why it was refused.</param>
	void DmRefused(UserSession sender, string recipientName, DmRefusal reason);
}

/// <summary>Why a private message was not delivered.</summary>
public enum DmRefusal : byte
{
	/// <summary>The recipient blocks the sender, or accepts messages from friends only.</summary>
	Blocked,

	/// <summary>The recipient is silenced and cannot take part in chat.</summary>
	Silenced
}
