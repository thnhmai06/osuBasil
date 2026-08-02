using Basil.Protocol.Irc;
using Basil.Protocol.Packets;

namespace Basil.Application.Sessions.Irc;

/// <summary>
///     The default <see cref="IIrcConnection" /> for a bancho <see cref="UserSession" />: it
///     re-encodes chat text routed through the IRC core back into a bancho SEND_MESSAGE packet,
///     enqueued for the client's next HTTP poll. Only PRIVMSG has a bancho equivalent; JOIN, PART,
///     QUIT, and numerics are IRC-only and ignored here, because bancho clients already receive
///     channel presence through ChannelInfo rather than per-user join and part events.
/// </summary>
public sealed class BanchoIrcBridgeConnection(UserSession userSession) : IIrcConnection
{
	/// <summary>Gets the userSession session this bridge sends chat on behalf of.</summary>
	public UserSession User { get; } = userSession;

	/// <summary>Gets a value that indicates whether the recipient is a real external IRC client; always false for this bridge.</summary>
	public bool IsExternalIrcClient => false;

	/// <summary>
	///     Converts a PRIVMSG into a bancho SEND_MESSAGE packet and enqueues it for the userSession's
	///     next HTTP poll. Every other IRC command is ignored, as is any message whose prefix cannot
	///     be parsed into a sender name and id.
	/// </summary>
	/// <param name="message">The IRC-shaped message to translate.</param>
	public void Send(IrcMessage message)
	{
		if (message.Command != "PRIVMSG") return;
		if (!IrcMessageWriter.TryParseUserPrefix(message.Prefix, out var senderName, out var senderId)) return;

		var recipient = TranslateRecipient(message.Params[0]);
		User.Enqueue(ServerPacketWriter.SendMessage(senderName, message.Params[1], recipient, senderId));
	}

	/// <summary>
	///     Translates an internal channel registry name into the alias the bancho client knows the
	///     channel as. A match or spectator channel's internal name (<c>#multi_{id}</c> or
	///     <c>#spec_{id}</c>) is never what the client joined: it only ever joined the fixed aliases
	///     <c>#multiplayer</c> and <c>#spectator</c>, because
	///     <see cref="Sessions.Channels.ChannelMembershipService.Join" /> sends
	///     <c>ChannelSession.DisplayName</c> rather than <c>Name</c>. Without this translation, a
	///     PRIVMSG addressed to the internal name would match no window the client has open and be
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