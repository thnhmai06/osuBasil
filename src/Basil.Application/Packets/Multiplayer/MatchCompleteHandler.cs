using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Packets.Multiplayer;

/// <summary>Handles the client's notification that the userSession has finished the current map.</summary>
/// <remarks>
///     Finalizes a round once every playing userSession has finished. The first completion only marks the
///     sending userSession's slot as <see cref="Basil.Domain.Multiplayer.SlotStatus.Complete" />; the round is
///     closed only when no slot is still <see cref="Basil.Domain.Multiplayer.SlotStatus.Playing" />.
///     Closing writes the round's EndedAt to the database via
///     <see cref="IMatchRepository.SetRoundEndedAsync" />, clears the match's InProgress,
///     unreadies the players, resets their loaded state, and broadcasts a <c>MatchComplete</c> packet
///     (to everyone except players who did not finish) followed by the updated match state. The round's
///     EndedAt is closed here immediately, but
///     <see cref="Basil.Application.Sessions.Multiplayer.MatchSession.CurrentRoundId" /> is deliberately
///     left set in memory: this small packet, which arrives over the persistent bancho connection,
///     routinely beats the corresponding score-submission HTTP POST (multipart, carries the replay,
///     arrives over a separate, unordered connection), so clearing it synchronously would drop the round
///     link for scores that are still in flight. The value only becomes stale once the next
///     <c>!mp start</c> overwrites it with a new round id. All mutations run under the match's
///     <see cref="Basil.Application.Sessions.Multiplayer.MatchSession.Lock" />.
/// </remarks>
public sealed class MatchCompleteHandler(
	MatchMembershipService matchMembership,
	IMatchRepository matchRepository,
	ILogger<MatchCompleteHandler> logger) : IPacketHandler
{
	/// <summary>Gets the client packet this handler processes.</summary>
	public ClientPackets PacketId => ClientPackets.MatchComplete;

	/// <summary>
	///     Gets a value that indicates whether the handler may run for restricted players. Always
	///     <see langword="false" />: round completion is not processed for restricted players.
	/// </summary>
	public bool AllowedWhenRestricted => false;

	/// <summary>Processes the match-complete packet for the given userSession.</summary>
	/// <param name="userSession">The userSession session that sent the packet.</param>
	/// <param name="reader">
	///		The packet reader positioned at the start of the payload; this handler does not read the payload.
	/// </param>
	/// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
	/// <returns>A task that completes when the packet has been handled.</returns>
	public async Task HandleAsync(UserSession userSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var match = userSession.Match;
		if (match is null)
		{
			logger.LogWarning("MatchComplete received but userSession has no active match: UserId={UserId}",
				userSession.Id);
			return;
		}

		await match.Lock.WaitAsync(cancellationToken);
		try
		{
			var slot = match.GetSlot(userSession.Id);
			if (slot is null)
			{
				logger.LogWarning(
					"MatchComplete received but userSession has no slot in the match: UserId={UserId} MatchId={MatchId}",
					userSession.Id, match.DbId);
				return;
			}

			slot.Status = SlotStatus.Complete;

			if (match.Slots.Any(s => s.Status == SlotStatus.Playing)) return;

			var notPlaying = match.Slots
				.Where(s => s.PlayerId is not null && s.Status != SlotStatus.Complete)
				.Select(s => s.PlayerId!.Value)
				.ToList();

			match.UnreadyPlayers(SlotStatus.Complete);
			match.ResetPlayersLoadedStatus();
			match.InProgress = false;

			var roundId = match.CurrentRoundId;
			if (roundId is { } id)
				await matchRepository.SetRoundEndedAsync(id, DateTimeOffset.UtcNow.UtcDateTime, false, cancellationToken);

			logger.LogInformation("~ Round complete: MatchId={MatchId} RoundId={RoundId}", match.DbId, roundId);
			matchMembership.Enqueue(match, ServerPacketWriter.MatchComplete(), false, notPlaying);
			await matchMembership.EnqueueStateAsync(match, cancellationToken: cancellationToken);
		}
		finally
		{
			match.Lock.Release();
		}
	}
}