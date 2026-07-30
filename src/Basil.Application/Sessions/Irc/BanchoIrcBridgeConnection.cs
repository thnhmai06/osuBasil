using Basil.Protocol.Irc;
using Basil.Protocol.Packets;

namespace Basil.Application.Sessions.Irc;

/// <summary>
///     Default <see cref="IIrcConnection" /> for every bancho <see cref="PlayerSession" /> — re-encodes chat text
///     routed through the IRC core back into a bancho SEND_MESSAGE packet, enqueued for the client's next
///     HTTP poll. Only PRIVMSG has a bancho equivalent; JOIN/PART/QUIT/numerics are IRC-only and ignored here
///     (bancho clients already get channel presence via ChannelInfo, not per-user join/part events).
/// </summary>
public sealed class BanchoIrcBridgeConnection(PlayerSession player) : IIrcConnection
{
	public PlayerSession Player { get; } = player;

	public bool IsExternalIrcClient => false;

	public void Send(IrcMessage message)
	{
		if (message.Command != "PRIVMSG") return;
		if (!IrcMessageWriter.TryParseUserPrefix(message.Prefix, out var senderName, out var senderId)) return;

		var recipient = TranslateRecipient(message.Params[0]);
		Player.Enqueue(ServerPacketWriter.SendMessage(senderName, message.Params[1], recipient, senderId));
	}

	/// <summary>
	///     A match/spectator channel's internal registry name (<c>#multi_{id}</c>/<c>#spec_{id}</c>)
	///     is never what the bancho client knows the channel as — it only ever joined the fixed alias
	///     <c>#multiplayer</c>/<c>#spectator</c> (<see cref="Sessions.Channels.ChannelMembershipService.Join" />
	///     sends <c>ChannelSession.DisplayName</c>, not <c>Name</c>). Without this translation, every
	///     PRIVMSG addressed to the internal name matches no window the client has open and is silently
	///     dropped — see bancho.py's <c>Channel.name</c> property, which performs the same aliasing for
	///     every outgoing packet.
	/// </summary>
	private string TranslateRecipient(string internalName)
	{
		if (internalName == Player.Match?.ChatChannelName) return "#multiplayer";

		var spectatorHostId = Player.Spectating?.Id ?? (Player.Spectators.Count > 0 ? Player.Id : (int?)null);
		if (spectatorHostId is { } hostId && internalName == $"#spec_{hostId}") return "#spectator";

		return internalName;
	}
}