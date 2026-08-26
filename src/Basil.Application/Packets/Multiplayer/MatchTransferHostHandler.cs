using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Packets.Multiplayer;

/// <summary>Handles the host's request to transfer the host role to another userSession.</summary>
/// <remarks>
///     Reads the target slot id and bounds-checks it against the fixed sixteen-slot layout. Only the
///     current host may transfer the role, and the target slot must be occupied. The match's HostId is
///     updated, a <c>MatchTransferHost</c> packet is enqueued for the new host, the updated state is
///     broadcast, and a <c>HostGranted</c> match event is persisted through
///     <see cref="IMatchRepository.CreateEventAsync" />. All of
///     this runs under the match's <see cref="Basil.Application.Sessions.Multiplayer.MatchSession.Lock" />.
/// </remarks>
public sealed class MatchTransferHostHandler(
	ISessionRegistry<GameSession> sessionRegistry,
	MatchMembershipService matchMembership,
	IMatchRepository matchRepository,
	ILogger<MatchTransferHostHandler> logger) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.MatchTransferHost;

	public bool AllowedWhenRestricted => false;

	public async Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var slotId = reader.ReadI32();

		var match = gameSession.Match;
		if (match is null || gameSession.Id != match.HostId || slotId is < 0 or >= 16) return;

		await match.Lock.WaitAsync(cancellationToken);
		try
		{
			var targetId = match.Slots[slotId].PlayerId;
			if (targetId is null) return;

			var prevHostId = match.HostId;
			match.HostId = targetId.Value;
			logger.LogInformation("Host transferred: MatchId={MatchId} PrevHostId={PrevHostId} NewHostId={NewHostId}",
				match.DbId, prevHostId, targetId.Value);

			var targetPlayer = sessionRegistry.GetByUserId(targetId.Value);
			targetPlayer?.Enqueue(ServerPacketWriter.MatchTransferHost());
			await matchMembership.EnqueueStateAsync(match, cancellationToken: cancellationToken);

			var prevHostName = sessionRegistry.GetByUserId(prevHostId)?.Name;
			await matchRepository.CreateEventAsync(new MatchEvent(
				match.DbId, (int)MatchEventType.HostGranted,
				prevHostId, prevHostName, targetId, targetPlayer?.Name,
				DateTimeOffset.UtcNow.UtcDateTime, null), cancellationToken);
		}
		finally
		{
			match.Lock.Release();
		}
	}
}