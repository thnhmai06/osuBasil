using OpenOsuTournament.Bancho.Application.Abstractions;
using OpenOsuTournament.Bancho.Application.Abstractions.Multiplayer;
using OpenOsuTournament.Bancho.Application.PacketHandlers.Core;
using OpenOsuTournament.Bancho.Application.Sessions;
using OpenOsuTournament.Bancho.Application.UseCases.Multiplayer;
using OpenOsuTournament.Bancho.Domain.Multiplayer;
using OpenOsuTournament.Bancho.Protocol.Packets;

namespace OpenOsuTournament.Bancho.Application.PacketHandlers.Multiplayer;

/// <summary>
///     Ported from app/api/domains/cho.py's MatchComplete. The `is_scrimming` branch
///     (asyncio.create_task(update_matchpoints(was_playing))) is dropped along with the rest of the
///     scrim engine — see note.md. Closes the round's EndedAt; does NOT gather/wait for score
///     submissions (see ScoreSubmissionUseCase's doc comment for why that isn't needed here).
/// </summary>
public sealed class MatchCompleteHandler(
    MatchMembershipService matchMembership,
    IMatchPersistenceRepository matchPersistence,
    IClock clock) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.MatchComplete;

    public bool AllowedWhenRestricted => false;

    public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
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

            if (match.CurrentRoundId is { } roundId)
            {
                await matchPersistence.SetRoundEndedAsync(roundId, clock.UtcNow.UtcDateTime);
                match.CurrentRoundId = null;
            }

            matchMembership.Enqueue(match, ServerPacketWriter.MatchComplete(), false, notPlaying);
            matchMembership.EnqueueState(match);
        }
        finally
        {
            match.Lock.Release();
        }
    }
}