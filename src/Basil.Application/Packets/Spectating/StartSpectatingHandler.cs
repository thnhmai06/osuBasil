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
	IUserSessionRegistry sessionRegistry,
	SpectatorService spectatorService,
	ILogger<StartSpectatingHandler> logger) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.StartSpectating;

	public bool AllowedWhenRestricted => false;

	public Task HandleAsync(UserSession userSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var targetId = reader.ReadI32();
		var newHost = sessionRegistry.GetById(targetId);
		if (newHost is null) return Task.CompletedTask;

		var currentHost = userSession.Spectating;
		if (currentHost is not null)
		{
			if (currentHost == newHost)
			{
				// Host hasn't changed — the client didn't have the map but has now downloaded
				// it. `userSession` already received the other fellow spectators, so no resend.
				logger.LogDebug("Spectator map re-download: UserId={UserId} HostId={NewHostId}",
					userSession.Id, newHost.Id);

				if (userSession.Stealth) return Task.CompletedTask;

				newHost.Enqueue(ServerPacketWriter.SpectatorJoined(userSession.Id));
				var joined = ServerPacketWriter.FellowSpectatorJoined(userSession.Id);
				foreach (var spec in newHost.Spectators)
					if (spec.Id != userSession.Id)
						spec.Enqueue(joined);

				return Task.CompletedTask;
			}

			spectatorService.RemoveSpectator(currentHost, userSession);
		}

		spectatorService.AddSpectator(newHost, userSession);
		return Task.CompletedTask;
	}
}