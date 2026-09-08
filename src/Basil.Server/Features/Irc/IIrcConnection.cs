using Basil.Server.Shared.Eventing;
// Basil.Server.Features.Chat is imported only so the <see cref="ChannelMembershipService" />
// below resolves; nothing here calls into Chat.
using Basil.Server.Features.Chat;
using Basil.Server.Shared.Sessions;
using Basil.Protocol.Irc;

namespace Basil.Server.Features.Irc;

/// <summary>
///     A transport-agnostic sink for IRC-shaped chat traffic bound to one
///     <see cref="UserSession" />, either a real TCP IRC client or a bridge that re-encodes into
///     bancho packets for an osu! client. Every <see cref="UserSession" /> has exactly one, so
///     <see cref="ChannelMembershipService" /> can broadcast chat text without
///     knowing which transport the recipient is actually connected through.
/// </summary>
public interface IIrcConnection
{
	/// <summary>Gets the session this connection carries chat for.</summary>
	UserSession User { get; }

	/// <summary>
	///     Sends an IRC-shaped message to the recipient. Must never block on I/O, matching
	///     <see cref="IMatchLiveEvents" />'s non-blocking publish contract.
	/// </summary>
	/// <param name="message">The IRC-shaped message to deliver.</param>
	void Send(IrcMessage message);
}