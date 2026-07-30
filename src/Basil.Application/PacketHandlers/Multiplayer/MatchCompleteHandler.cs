using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.PacketHandlers.Core;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging;

namespace Basil.Application.PacketHandlers.Multiplayer;

/// <summary>
///     Ported from app/api/domains/cho.py's MatchComplete. The `is_scrimming` branch
///     (asyncio.create_task(update_matchpoints(was_playing))) is dropped along with the rest of the
///     scrim engine — see note.md. Closes the round's EndedAt; does NOT gather/wait for score
///     submissions (see ScoreSubmissionService's doc comment for why that isn't needed here).
/// </summary>
public sealed class MatchCompleteHandler(
	MatchMembershipService matchMembership,
	IMatchPersistenceRepository matchPersistence,
	ILogger<MatchCompleteHandler> logger) : IBanchoPacketHandler
{
	public ClientPackets PacketId => ClientPackets.MatchComplete;

	public bool AllowedWhenRestricted => false;

	public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var match = player.Match;
		if (match is null) return;

		await match.Lock.WaitAsync();
		try
		{
			var slot = match.GetSlot(player.Id);
			if (slot is null) return;

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
			{
				await matchPersistence.SetRoundEndedAsync(id, DateTimeOffset.UtcNow.UtcDateTime, false);
				match.CurrentRoundId = null;
			}

			logger.LogInformation("Round complete: MatchId={MatchId} RoundId={RoundId}", match.DbId, roundId);
			matchMembership.Enqueue(match, ServerPacketWriter.MatchComplete(), false, notPlaying);
			await matchMembership.EnqueueStateAsync(match);
		}
		finally
		{
			match.Lock.Release();
		}
	}
}