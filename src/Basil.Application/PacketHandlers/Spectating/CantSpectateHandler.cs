using Basil.Application.PacketHandlers.Core;
using Basil.Application.Sessions;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging;

namespace Basil.Application.PacketHandlers.Spectating;

/// <summary>Ported from app/api/domains/cho.py's CantSpectate.</summary>
public sealed class CantSpectateHandler(ILogger<CantSpectateHandler> logger) : IBanchoPacketHandler
{
	public ClientPackets PacketId => ClientPackets.CantSpectate;

	public bool AllowedWhenRestricted => false;

	public Task HandleAsync(PlayerSession player, BanchoPacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var host = player.Spectating;
		if (host is null || player.Stealth) return Task.CompletedTask;

		logger.LogDebug("Client cannot spectate (missing map): UserId={UserId} HostId={HostId}", player.Id, host.Id);
		var packet = ServerPacketWriter.SpectatorCantSpectate(player.Id);
		host.Enqueue(packet);

		foreach (var spectator in host.Spectators) spectator.Enqueue(packet);

		return Task.CompletedTask;
	}
}