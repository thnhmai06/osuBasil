using Basil.Application.PacketHandlers.Core;
using Basil.Application.Services.Spectating;
using Basil.Application.Sessions;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging;

namespace Basil.Application.PacketHandlers.Spectating;

/// <summary>Ported from app/api/domains/cho.py's StartSpectating.</summary>
public sealed class StartSpectatingHandler(
	IPlayerSessionRegistry sessionRegistry,
	SpectatorService spectatorService,
	ILogger<StartSpectatingHandler> logger) : IBanchoPacketHandler
{
	public ClientPackets PacketId => ClientPackets.StartSpectating;

	public bool AllowedWhenRestricted => false;

	public Task HandleAsync(PlayerSession player, BanchoPacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var targetId = reader.ReadI32();
		var newHost = sessionRegistry.GetById(targetId);
		if (newHost is null) return Task.CompletedTask;

		var currentHost = player.Spectating;
		if (currentHost is not null)
		{
			if (currentHost == newHost)
			{
				// Host hasn't changed — the client didn't have the map but has now downloaded
				// it. `player` already received the other fellow spectators, so no resend.
				logger.LogDebug("Spectator map re-download: UserId={UserId} HostId={NewHostId}",
					player.Id, newHost.Id);
				if (!player.Stealth)
				{
					newHost.Enqueue(ServerPacketWriter.SpectatorJoined(player.Id));

					var joined = ServerPacketWriter.FellowSpectatorJoined(player.Id);
					foreach (var spec in newHost.Spectators)
						if (spec.Id != player.Id)
							spec.Enqueue(joined);
				}

				return Task.CompletedTask;
			}

			spectatorService.RemoveSpectator(currentHost, player);
		}

		spectatorService.AddSpectator(newHost, player);
		return Task.CompletedTask;
	}
}