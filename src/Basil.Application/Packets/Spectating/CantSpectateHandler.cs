using Basil.Application.Sessions;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Packets.Spectating;

/// <summary>
///     Handles the <see cref="ClientPackets.CantSpectate" /> packet, which the client sends when it
///     cannot spectate its target, usually because the target's current map is missing. Notifies the
///     spectated host and every fellow spectator that this userSession cannot spectate.
/// </summary>
/// <remarks>
///     The notification is skipped when the userSession is not currently spectating anyone or is in stealth
///     mode. Each recipient receives the same spectator-cant-spectate packet addressed to this
///     userSession's id.
/// </remarks>
public sealed class CantSpectateHandler(ILogger<CantSpectateHandler> logger) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.CantSpectate;

	public bool AllowedWhenRestricted => false;

	public Task HandleAsync(GameSession userSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var host = userSession.Spectating;
		if (host is null || userSession.Stealth) return Task.CompletedTask;

		logger.LogDebug(
			"Client cannot spectate (missing map): UserId={UserId} HostId={HostId}", userSession.Id, host.Id);
		var packet = ServerPacketWriter.SpectatorCantSpectate(userSession.Id);
		host.Enqueue(packet);

		foreach (var spectator in host.Spectators)
			spectator.Enqueue(packet);

		return Task.CompletedTask;
	}
}