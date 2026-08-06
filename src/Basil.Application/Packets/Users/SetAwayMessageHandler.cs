using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.Packets.Users;

/// <summary>
///     Handles the SetAwayMessage packet, which stores an away message on the userSession's session.
/// </summary>
/// <remarks>
///     The body is an osu! chat message read via <see cref="PacketReader.ReadMessage" />; only
///     its text is kept and stored on <see cref="UserSession.AwayMessage" />.
/// </remarks>
public sealed class SetAwayMessageHandler : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.SetAwayMessage;

	public bool AllowedWhenRestricted => false;

	public Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var message = reader.ReadMessage();
		gameSession.AwayMessage = message.Text;
		return Task.CompletedTask;
	}
}