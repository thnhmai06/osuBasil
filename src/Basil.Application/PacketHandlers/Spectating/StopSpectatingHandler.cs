using Basil.Application.PacketHandlers.Core;
using Basil.Application.Services.Spectating;
using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Spectating;

/// <summary>
///     Handles the <see cref="ClientPackets.StopSpectating" /> packet, which the client sends to stop
///     spectating. Removes the player from its current spectate target through
///     <see cref="SpectatorService.RemoveSpectator" />.
/// </summary>
/// <remarks>
///     The packet is a no-op when the player is not currently spectating anyone.
/// </remarks>
public sealed class StopSpectatingHandler(SpectatorService spectatorService) : IBanchoPacketHandler
{
	public ClientPackets PacketId => ClientPackets.StopSpectating;

	public bool AllowedWhenRestricted => false;

	public Task HandleAsync(PlayerSession player, BanchoPacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var host = player.Spectating;
		if (host is not null) spectatorService.RemoveSpectator(host, player);

		return Task.CompletedTask;
	}
}