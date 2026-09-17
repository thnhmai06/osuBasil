using Basil.Application.Multiplayer;
using Basil.Application.Sessions;
using Basil.Domain.Multiplayer.Records;
using Basil.Host.Bancho.Shared.Http;
using Basil.Protocol.Packets;

namespace Basil.Host.Bancho.Multiplayer.Packets;

/// <summary>Handles the host's request to transfer the host role to another userSession.</summary>
/// <remarks>
///     Reads the target slot id and bounds-checks it against the fixed sixteen-slot layout. Only the
///     current host may transfer the role, and the target slot must be occupied. The match's Host is
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
		if (match is null || gameSession.Id != match.Host?.Id || slotId is < 0 or >= 16) return;

		await using var mutation = await match.BeginMutationAsync(cancellationToken);

		// Host status is re-checked here, not just before the lock: it can only change under this
		// same lock (a concurrent leave or transfer), so a sender who lost host while waiting for
		// it must not still be treated as authoritative once the lock is acquired.
		if (gameSession.Id != match.Host?.Id) return;

		var target = match.Slots[slotId].Player;
		if (target is null) return;

		var prevHost = match.Host;
		match.Host = target;
		logger.LogInformation("Host transferred: MatchId={MatchId} PrevHostId={PrevHostId} NewHostId={NewHostId}",
			match.DbId, prevHost.Id, target.Id);

		var targetPlayer = sessionRegistry.GetByUserId(target.Id);
		targetPlayer?.Enqueue(ServerPacketWriter.MatchTransferHost());

		// Runs here, still under the lock, rather than after: this audit-trail write doesn't read or
		// depend on live match state beyond values already captured above.
		// gameSession is prevHost's session: verified by the guard above.
		await matchRepository.CreateEventAsync(new MatchEvent(
			match.DbId, MatchEventType.HostGranted,
			prevHost, target,
			DateTimeOffset.UtcNow.UtcDateTime, null), cancellationToken);

		mutation.PublishState();
	}
}