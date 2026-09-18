using Basil.Application.Sessions;
using Basil.Protocol.Irc;
using Basil.Protocol.Packets;

namespace Basil.Application.Irc;

/// <summary>
///     The default <see cref="IIrcConnection" /> for a <see cref="GameSession" />: it re-encodes
///     chat text routed through the IRC core back into a bancho SEND_MESSAGE packet, enqueued for
///     the client's next HTTP poll. Only PRIVMSG and NOTICE have a bancho equivalent; JOIN, PART,
///     QUIT, and numerics are IRC-only and ignored here, because bancho clients already receive
///     channel presence through ChannelInfo rather than per-user join and part events.
/// </summary>
/// <remarks>
///     Stays in <c>Basil.Application</c> rather than moving to <c>Basil.Host.Bancho</c> with the
///     rest of the bancho packet-transport seam (Batch 11): <see cref="GameSession" />'s constructor
///     self-wires this as its default <see cref="IIrcConnection" />, which needs a concrete type
///     Application can construct directly. Its <c>Basil.Protocol</c> dependency is exactly why it
///     stays on <see cref="Basil.ArchitectureTests.TransportSeamTests" />'s pinned exception list
///     instead.
/// </remarks>
public sealed class BanchoIrcBridgeConnection(GameSession userSession) : IIrcConnection
{
	/// <summary>Gets the user session this bridge sends chat on behalf of.</summary>
	public GameSession User { get; } = userSession;

	/// <inheritdoc />
	UserSession IIrcConnection.User => User;

	/// <summary>
	///     Converts a PRIVMSG or NOTICE into a bancho SEND_MESSAGE packet and enqueues it for the
	///     userSession's next HTTP poll. Every other IRC command is ignored, as is any message whose
	///     prefix cannot be parsed into a sender name and id.
	/// </summary>
	/// <param name="message">The IRC-shaped message to translate.</param>
	public void Send(IrcMessage message)
	{
		// A bancho client has no notice concept, so a notice reaches it as an ordinary chat line
		// rather than not at all.
		if (message.Command is not ("PRIVMSG" or "NOTICE")) return;
		if (!IrcMessageWriter.TryParseUserPrefix(message.Prefix, out var senderName, out var senderId, out _)) return;

		var recipient = TranslateRecipient(message.Params[0]);
		User.Enqueue(ServerPacketWriter.SendMessage(senderName, message.Params[1], recipient, senderId));
	}

	/// <summary>
	///     Translates an internal channel registry name into the alias the bancho client knows the
	///     channel as. A match or spectator channel's internal name (<c>#mp_{id}</c> or
	///     <c>#spec_{id}</c>) is never what the client joined: it only ever joined the fixed aliases
	///     <c>#multiplayer</c> and <c>#spectator</c>, because Chat's ChannelMembershipService.Url
	///     sends <c>ChannelSession.DisplayName</c> rather than <c>Name</c>. Without this translation, a
	///     PRIVMSG addressed to the internal name would match no window the client has opened and be
	///     silently dropped.
	/// </summary>
	/// <param name="internalName">The internal registry name of the message's recipient channel.</param>
	/// <returns>
	///     The client-facing alias for the channel, or <paramref name="internalName" /> unchanged when it is not an
	///     instance channel.
	/// </returns>
	private string TranslateRecipient(string internalName)
	{
		if (internalName == User.Match?.ChatChannelName) return "#multiplayer";

		var spectatorHostId = User.Spectating?.Id ?? (User.Spectators.Count > 0 ? User.Id : null);
		if (spectatorHostId is { } hostId && internalName == $"#spec_{hostId}") return "#spectator";

		return internalName;
	}
}