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
///     <see cref="IMatchRepository.CreateEventAsync" />, without awaiting the write. All of
///     this runs under the match's <see cref="Basil.Application.Sessions.Multiplayer.MatchSession.Lock" />.
/// </remarks>
public sealed class MatchTransferHostHandler(
	IUserSessionRegistry sessionRegistry,
	MatchMembershipService matchMembership,
	IMatchRepository matchRepository,
	ILogger<MatchTransferHostHandler> logger) : IPacketHandler
{
	/// <summary>Gets the client packet this handler processes.</summary>
	public ClientPackets PacketId => ClientPackets.MatchTransferHost;

	/// <summary>
	///     Gets a value that indicates whether the handler may run for restricted players. Always
	///     <see langword="false" />: host transfers are not processed for restricted players.
	/// </summary>
	public bool AllowedWhenRestricted => false;

	/// <summary>Processes the transfer-host packet for the given userSession.</summary>
	/// <param name="userSession">The userSession session that sent the packet.</param>
	/// <param name="reader">The packet reader positioned at the payload holding the target slot id.</param>
	/// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
	/// <returns>A task that completes when the packet has been handled.</returns>
	public async Task HandleAsync(GameSession userSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var slotId = reader.ReadI32();

		var match = userSession.Match;
		if (match is null || userSession.Id != match.HostId || slotId is < 0 or >= 16) return;

		await match.Lock.WaitAsync(cancellationToken);
		try
		{
			var targetId = match.Slots[slotId].PlayerId;
			if (targetId is null) return;

			var prevHostId = match.HostId;
			match.AssignGameplayHost(targetId.Value);
			logger.LogInformation("Host transferred: MatchId={MatchId} PrevHostId={PrevHostId} NewHostId={NewHostId}",
				match.DbId, prevHostId, targetId.Value);

			var targetPlayer = sessionRegistry.GetGameByUserId(targetId.Value);
			targetPlayer?.Enqueue(ServerPacketWriter.MatchTransferHost());
			await matchMembership.EnqueueStateAsync(match, cancellationToken: cancellationToken);

			var prevHostName = sessionRegistry.GetGameByUserId(prevHostId)?.Name;
			_ = matchRepository.CreateEventAsync(new MatchEvent(
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