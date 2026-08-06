using Basil.Application.Services.Spectating;
using Basil.Application.Sessions;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Packets.Spectating;

/// <summary>
///     Handles the <see cref="ClientPackets.StartSpectating" /> packet, which the client sends to
///     start spectating another userSession. Reads the target's id, resolves the target session, and
///     switches the userSession's spectating target through <see cref="SpectatorService" />.
/// </summary>
/// <remarks>
///     When the userSession already spectates the requested host, the packet is treated as a map
///     re-download and the host and fellow spectators are re-notified, unless the userSession is in stealth
///     mode. When the userSession spectates a different host, it is removed from that host first. A request
///     for an unknown target id is ignored.
/// </remarks>
public sealed class StartSpectatingHandler(
	ISessionRegistry<GameSession> sessionRegistry,
	SpectatorService spectatorService,
	ILogger<StartSpectatingHandler> logger) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.StartSpectating;

	public bool AllowedWhenRestricted => false;

	public Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var targetId = reader.ReadI32();
		var newHost = sessionRegistry.GetByUserId(targetId);
		if (newHost is null) return Task.CompletedTask;

		var currentHost = gameSession.Spectating;
		if (currentHost is not null)
		{
			if (currentHost == newHost)
			{
				// Host hasn't changed — the client didn't have the map but has now downloaded
				// it. `userSession` already received the other fellow spectators, so no resend.
				logger.LogDebug("Spectator map re-download: UserId={UserId} HostId={NewHostId}",
					gameSession.Id, newHost.Id);

				if (gameSession.Stealth) return Task.CompletedTask;

				newHost.Enqueue(ServerPacketWriter.SpectatorJoined(gameSession.Id));
				var joined = ServerPacketWriter.FellowSpectatorJoined(gameSession.Id);
				foreach (var spec in newHost.Spectators)
					if (spec.Id != gameSession.Id)
						spec.Enqueue(joined);

				return Task.CompletedTask;
			}

			spectatorService.RemoveSpectator(currentHost, gameSession);
		}

		spectatorService.AddSpectator(newHost, gameSession);
		return Task.CompletedTask;
	}
}