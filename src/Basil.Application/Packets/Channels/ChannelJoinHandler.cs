using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Protocol.Packets;

namespace Basil.Application.Packets.Channels;

/// <summary>
///     Handles the <see cref="ClientPackets.ChannelJoin" /> packet, which the client sends when it
///     wants to join a chat channel. Reads the channel name, ignores the client-managed virtual
///     channels, and delegates the membership update to <see cref="ChannelMembershipService" /> when
///     the named channel exists.
/// </summary>
/// <remarks>
///     Login only sends channel_info for the auto-join channels; the client is expected to send this
///     packet itself to actually join one of them. The join logic in
///     <see cref="ChannelMembershipService" /> also broadcasts the updated channel_info and is shared
///     with server-initiated joins (spectator, multiplayer) and real IRC connections.
/// </remarks>
public sealed class ChannelJoinHandler(IChannelRegistry channelRegistry, ChannelMembershipService channelMembership)
	: IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.ChannelJoin;

	public bool AllowedWhenRestricted => true;

	public Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var name = reader.ReadString();

		if (name is "#highlight" or "#userlog") return Task.CompletedTask; // Local client channel

		var channel = channelRegistry.GetByName(name);
		if (channel is not null) channelMembership.Join(gameSession, channel);

		return Task.CompletedTask;
	}
}