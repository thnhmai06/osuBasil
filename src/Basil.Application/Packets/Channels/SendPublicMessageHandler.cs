using Basil.Application.Services.Chat;
using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.Packets.Channels;

/// <summary>
///     Handles the <see cref="ClientPackets.SendPublicMessage" /> packet, which carries a message
///     addressed to a chat channel. Reads the message and routes it through
///     <see cref="ChatDispatchService.SendPrivmsgAsync" />.
/// </summary>
/// <remarks>
///     All routing, broadcast, and command-dispatch logic lives in <see cref="ChatDispatchService" />,
///     shared with private messages and real IRC PRIVMSG. A bancho client, an IRC client, and BanchoBot
///     all go through the same chat core.
/// </remarks>
public sealed class SendPublicMessageHandler(ChatDispatchService chatDispatch) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.SendPublicMessage;

	public bool AllowedWhenRestricted => true;

	public async Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var message = reader.ReadMessage();
		await chatDispatch.SendPrivmsgAsync(gameSession, message.Recipient, message.Text, cancellationToken);
	}
}