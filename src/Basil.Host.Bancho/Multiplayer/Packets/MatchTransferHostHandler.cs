using Basil.Application.Multiplayer;
using Basil.Application.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Host.Bancho.Shared.Http;
using Basil.Infrastructure.Shared.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Host.Bancho.Multiplayer.Packets;

/// <summary>Handles the host's request to transfer the host role to another userSession.</summary>
/// <remarks>
///     Reads the target slot id and bounds-checks it against the fixed sixteen-slot layout. Only the
///     current host may transfer the role, and the target slot must be occupied. The match's HostId is
///     updated, a <c>MatchTransferHost</c> packet is enqueued for the new host, the updated state is
///     broadcast, and a <c>HostGranted</c> match event is persisted through
///     <see cref="IMatchRepository.CreateEventAsync" />. All of
///     this runs under the match's <see cref="MatchSession.Lock" />.
/// </remarks>
public sealed class MatchTransferHostHandler(
	ISessionRegistry<GameSession> sessionRegistry,
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

		await using var mutation = await match.BeginMutationAsync(cancellationToken);

		// Host status is re-checked here, not just before the lock: it can only change under this
		// same lock (a concurrent leave or transfer), so a sender who lost host while waiting for
		// it must not still be treated as authoritative once the lock is acquired.
		if (gameSession.Id != match.HostId) return;

		var targetId = match.Slots[slotId].PlayerId;
		if (targetId is null) return;

		var prevHostId = match.HostId;
		match.HostId = targetId.Value;
		logger.LogInformation("Host transferred: MatchId={MatchId} PrevHostId={PrevHostId} NewHostId={NewHostId}",
			match.DbId, prevHostId, targetId.Value);

		var targetPlayer = sessionRegistry.GetByUserId(targetId.Value);
		targetPlayer?.Enqueue(ServerPacketWriter.MatchTransferHost());

		// Runs here, still under the lock, rather than after: this audit-trail write doesn't read or
		// depend on live match state beyond values already captured above.
		// gameSession is prevHostId's session: verified by the guard above.
		var prevHostName = gameSession.Name;
		await matchRepository.CreateEventAsync(new MatchEvent(
			match.DbId, (int)MatchEventType.HostGranted,
			prevHostId, prevHostName, targetId, targetPlayer?.Name,
			DateTimeOffset.UtcNow.UtcDateTime, null), cancellationToken);

		mutation.PublishState();
	}
}