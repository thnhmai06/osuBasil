using Bancho.Application.Sessions;
using Bancho.Application.UseCases.Multiplayer;
using Bancho.Domain;
using Bancho.Protocol;

namespace Bancho.Application.PacketHandlers;

/// <summary>
/// Ported from app/api/domains/cho.py's MatchComplete, including the `is_scrimming` branch
/// (asyncio.create_task(update_matchpoints(was_playing))). The scoring task is launched only
/// AFTER this handler releases MatchSession.Lock — starting it from inside the lock would mean
/// its first act (re-acquiring that same lock) blocks on the launcher, and if it "worked" anyway
/// it would hold the lock across the up-to-10s score-submission poll, freezing every other slot
/// mutation on the match. See MatchSession's doc comment.
/// </summary>
public sealed class MatchCompleteHandler(MatchMembershipService matchMembership, MatchScoringService scoringService) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.MatchComplete;

    public bool AllowedWhenRestricted => false;

    public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var match = player.Match;
        if (match is null)
        {
            return;
        }

        MatchRoundSnapshot? snapshot = null;

        await match.Lock.WaitAsync();
        try
        {
            var slot = match.GetSlot(player.Id);
            if (slot is null)
            {
                return;
            }

            slot.Status = SlotStatus.Complete;

            if (match.Slots.Any(s => s.Status == SlotStatus.Playing))
            {
                return;
            }

            var notPlaying = match.Slots
                .Where(s => s.PlayerId is not null && s.Status != SlotStatus.Complete)
                .Select(s => s.PlayerId!.Value)
                .ToList();

            var wasPlaying = match.Slots
                .Where(s => s.PlayerId is not null && !notPlaying.Contains(s.PlayerId!.Value))
                .Select(s => (s.PlayerId!.Value, s.Team))
                .ToList();

            match.UnreadyPlayers(SlotStatus.Complete);
            match.ResetPlayersLoadedStatus();
            match.InProgress = false;

            matchMembership.Enqueue(match, ServerPacketWriter.MatchComplete(), lobby: false, immune: notPlaying);
            matchMembership.EnqueueState(match);

            if (match.IsScrimming)
            {
                snapshot = new MatchRoundSnapshot(wasPlaying, match.MapMd5, match.TeamType, match.WinCondition, match.Name);
            }
        }
        finally
        {
            match.Lock.Release();
        }

        if (snapshot is not null)
        {
            _ = scoringService.ScoreCompletedRoundAsync(match, snapshot);
        }
    }
}
