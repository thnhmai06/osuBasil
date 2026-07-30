using Basil.Application.PacketHandlers.Core;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Channels;

/// <summary>Ported from app/api/domains/cho.py's PartLobby: leaves `#lobby`, no kick packet needed.</summary>
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
