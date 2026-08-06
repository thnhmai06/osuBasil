using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Multiplayer;
using Basil.Protocol.Packets;

namespace Basil.Application.Packets.Channels;

/// <summary>
///     Handles the <see cref="ClientPackets.JoinLobby" /> packet, which the client sends when it
///     enters the multiplayer lobby screen. Marks the userSession as being in the lobby, joins the
///     `#lobby` channel, and sends every currently active match to the client.
/// </summary>
/// <remarks>
///     The client requests the lobby list only once on entering the screen rather than polling, so
///     the handler sends the full set of active matches from <see cref="IMatchRegistry.All" /> in a
///     single burst, each encoded through <see cref="MatchPacketDataMapper" />.
/// </remarks>
public sealed class LobbyJoinHandler(
	IChannelRegistry channelRegistry,
	ChannelMembershipService channelMembership,
	IMatchRegistry matchRegistry) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.JoinLobby;

	public bool AllowedWhenRestricted => true;

	public Task HandleAsync(GameSession userSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		userSession.InLobby = true;

		var lobby = channelRegistry.GetByName("#lobby");
		if (lobby is not null) channelMembership.Join(userSession, lobby);

		foreach (var match in matchRegistry.All)
			userSession.Enqueue(ServerPacketWriter.NewMatch(match.ToPacket()));

		return Task.CompletedTask;
	}
}