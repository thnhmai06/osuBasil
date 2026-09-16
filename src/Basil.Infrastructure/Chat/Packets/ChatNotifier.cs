using Basil.Application.Sessions;
using Basil.Application.Shared.Configuration;
using Basil.Infrastructure.Shared.Sessions;
using Basil.Protocol.Irc;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Chat.Packets;

/// <summary>
///     Delivers chat through the recipient's session connection, which speaks IRC lines to an IRC
///     client and re-encodes them as bancho packets for an osu! client.
/// </summary>
public sealed class ChatNotifier(IOptions<IrcOptions> options) : IChatNotifier
{
	public void Deliver(UserSession recipient, ChatLine line)
	{
		recipient.IrcConnection.Send(line.Notice
			? IrcMessageWriter.Notice(options.Value.Name, line.SenderName, line.SenderId, line.Target, line.Text)
			: IrcMessageWriter.Privmsg(options.Value.Name, line.SenderName, line.SenderId, line.Target, line.Text));
	}

	/// <remarks>Only an osu! client has a way to hear this; an IRC sender is told nothing, as before.</remarks>
	public void DmRefused(UserSession sender, string recipientName, DmRefusal reason)
	{
		if (sender is not GameSession game) return;

		game.Enqueue(reason == DmRefusal.Silenced
			? ServerPacketWriter.TargetSilenced(recipientName)
			: ServerPacketWriter.UserDmBlocked(recipientName));
	}
}
