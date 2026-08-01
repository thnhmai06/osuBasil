using Basil.Application.PacketHandlers.Core;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Channels;

/// <summary>
///     Handles the <see cref="ClientPackets.ChannelPart" /> packet, which the client sends when it
///     wants to leave a chat channel. Reads the channel name, ignores the client-managed virtual
///     channels, and calls <see cref="ChannelMembershipService.Part" /> when the named channel exists.
/// </summary>
/// <remarks>
///     The part is performed without a kick packet: the client already knows it left because it sent
///     this packet, unlike a server-initiated part, which does need one. The membership logic in
///     <see cref="ChannelMembershipService" /> broadcasts the updated channel_info and the IRC-shaped
///     PART message to the remaining members.
/// </remarks>
public sealed class ChannelPartHandler(IChannelRegistry channelRegistry, ChannelMembershipService channelMembership)
	: IBanchoPacketHandler
{
	public ClientPackets PacketId => ClientPackets.ChannelPart;

	public bool AllowedWhenRestricted => true;

	public Task HandleAsync(PlayerSession player, BanchoPacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var name = reader.ReadString();

		if (name is "#highlight" or "#userlog") return Task.CompletedTask;

		var channel = channelRegistry.GetByName(name);
		if (channel is not null) channelMembership.Part(player, channel, false);

		return Task.CompletedTask;
	}
}