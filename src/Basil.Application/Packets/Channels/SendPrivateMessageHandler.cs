using Basil.Application.Services.Chat;
using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.Packets.Channels;

/// <summary>
///     Handles the <see cref="ClientPackets.SendPrivateMessage" /> packet, which carries a private
///     message addressed to a single user. Reads the message and routes it through
///     <see cref="ChatDispatchService.SendPrivmsgAsync" />.
/// </summary>
/// <remarks>
///     All routing and delivery checks, including the bot command shortcut and the block, pm-private,
///     and silence checks, live in <see cref="ChatDispatchService" />. The same service is shared with
///     public messages and real IRC PRIVMSG.
/// </remarks>
public sealed class SendPrivateMessageHandler(ChatDispatchService chatDispatch) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.SendPrivateMessage;

	public bool AllowedWhenRestricted => true;

	public async Task HandleAsync(GameSession userSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var message = reader.ReadMessage();
		await chatDispatch.SendPrivmsgAsync(userSession, message.Recipient, message.Text, cancellationToken);
	}
}