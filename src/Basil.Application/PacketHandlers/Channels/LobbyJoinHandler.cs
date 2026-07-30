using Basil.Application.PacketHandlers.Core;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Multiplayer;
using Basil.Application.Services.Multiplayer;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Channels;

/// <summary>
///     Ported from app/api/domains/cho.py's JoinLobby: joins `#lobby` and sends the client every
///     currently-active match, since the client only requests this once (on entering the mp lobby
///     screen) rather than polling.
/// </summary>
public sealed class LobbyJoinHandler(
	IChannelRegistry channelRegistry,
	ChannelMembershipService channelMembership,
	IMatchRegistry matchRegistry) : IBanchoPacketHandler
{
	public ClientPackets PacketId => ClientPackets.JoinLobby;

	public bool AllowedWhenRestricted => true;

	public Task HandleAsync(PlayerSession player, BanchoPacketReader reader,
		CancellationToken cancellationToken = default)
	{
		player.InLobby = true;

		var lobby = channelRegistry.GetByName("#lobby");
		if (lobby is not null) channelMembership.Join(player, lobby);

		foreach (var match in matchRegistry.All)
			player.Enqueue(ServerPacketWriter.NewMatch(MatchPacketDataMapper.ToPacketData(match)));

		return Task.CompletedTask;
	}
}
