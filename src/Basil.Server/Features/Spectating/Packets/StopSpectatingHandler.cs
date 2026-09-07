using Basil.Server.Shared.Http.Bancho;
using Basil.Server.Features.Spectating;
using Basil.Server.Shared.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Server.Features.Spectating.Packets;

/// <summary>
///     Handles the <see cref="ClientPackets.StopSpectating" /> packet, which the client sends to stop
///     spectating. Removes the userSession from its current spectating target through
///     <see cref="SpectatorService.RemoveSpectator" />.
/// </summary>
/// <remarks>
///     The packet is a no-op when the userSession is not currently spectating anyone.
/// </remarks>
public sealed class StopSpectatingHandler(SpectatorService spectatorService) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.StopSpectating;

	public bool AllowedWhenRestricted => false;

	public Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var host = gameSession.Spectating;
		if (host is not null) spectatorService.RemoveSpectator(host, gameSession);

		return Task.CompletedTask;
	}
}