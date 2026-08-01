using Basil.Application.PacketHandlers.Core;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Channels;

/// <summary>
///     Handles the <see cref="ClientPackets.PartLobby" /> packet, which the client sends when it
///     leaves the multiplayer lobby screen. Marks the player as no longer in the lobby and parts the
///     `#lobby` channel.
/// </summary>
/// <remarks>
///     The part is performed without a kick packet because the client already knows it left the
///     channel by sending this packet.
/// </remarks>
public sealed class LobbyPartHandler(IChannelRegistry channelRegistry, ChannelMembershipService channelMembership)
	: IBanchoPacketHandler
{
	public ClientPackets PacketId => ClientPackets.PartLobby;

	public bool AllowedWhenRestricted => true;

	public Task HandleAsync(PlayerSession player, BanchoPacketReader reader,
		CancellationToken cancellationToken = default)
	{
		player.InLobby = false;

		var lobby = channelRegistry.GetByName("#lobby");
		if (lobby is not null) channelMembership.Part(player, lobby, false);

		return Task.CompletedTask;
	}
}