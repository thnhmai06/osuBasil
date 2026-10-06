using Basil.Application.Channels;
using Basil.Application.Chat;
using Basil.Application.Multiplayer;
using Basil.Application.Sessions;
using Basil.Host.Bancho.Multiplayer;
using Basil.Host.Bancho.Shared.Http;
using Basil.Protocol.Bancho.Packets;

namespace Basil.Host.Bancho.Chat.Packets;

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

	public Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		gameSession.InLobby = true;

		var lobby = channelRegistry.GetByName("#lobby");
		if (lobby is not null) channelMembership.Join(gameSession, lobby);

		foreach (var match in matchRegistry.All)
			gameSession.Enqueue(PacketWriter.NewMatch(match.ToPacket()));

		return Task.CompletedTask;
	}
}