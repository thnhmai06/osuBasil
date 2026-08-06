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
	/// <summary>The <see cref="ClientPackets.SetAwayMessage" /> packet type.</summary>
	public ClientPackets PacketId => ClientPackets.SetAwayMessage;

	/// <summary>Restricted players may not set an away message, so this handler is unavailable to them.</summary>
	public bool AllowedWhenRestricted => false;

	/// <summary>Reads the message and stores its text as the userSession's away message.</summary>
	/// <param name="gameSession">The userSession session whose away message is being set.</param>
	/// <param name="reader">The packet reader positioned at the SetAwayMessage body.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>A completed task.</returns>
	public Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var message = reader.ReadMessage();
		gameSession.AwayMessage = message.Text;
		return Task.CompletedTask;
	}
}