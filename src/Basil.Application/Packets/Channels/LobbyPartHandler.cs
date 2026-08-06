using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Protocol.Packets;

namespace Basil.Application.Packets.Channels;

/// <summary>
///     Handles the <see cref="ClientPackets.PartLobby" /> packet, which the client sends when it
///     leaves the multiplayer lobby screen. Marks the userSession as no longer in the lobby and parts the
///     `#lobby` channel.
/// </summary>
/// <remarks>
///     The part is performed without a kick packet because the client already knows it left the
///     channel by sending this packet.
/// </remarks>
public sealed class LobbyPartHandler(IChannelRegistry channelRegistry, ChannelMembershipService channelMembership)
	: IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.PartLobby;

	public bool AllowedWhenRestricted => true;

	public Task HandleAsync(GameSession userSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		userSession.InLobby = false;

		var lobby = channelRegistry.GetByName("#lobby");
		if (lobby is not null) channelMembership.Part(userSession, lobby, false);

		return Task.CompletedTask;
	}
}