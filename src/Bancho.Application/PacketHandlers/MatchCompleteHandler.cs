using Bancho.Application.Sessions;
using Bancho.Application.UseCases.Multiplayer;
using Bancho.Domain;
using Bancho.Protocol;

namespace Bancho.Application.PacketHandlers;

/// <summary>
/// Ported from app/api/domains/cho.py's MatchComplete. The `is_scrimming` branch (kick off
/// `update_matchpoints`/`await_submissions` to determine a scrim winner) is not ported — scrim
/// state isn't on MatchSession yet, so `IsScrimming` is always false and that branch is dead code
/// for every match today, exactly matching the Python source's behavior until scrim lands.
/// </summary>
public sealed class MatchCompleteHandler(MatchMembershipService matchMembership) : IBanchoPacketHandler
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

            match.UnreadyPlayers(SlotStatus.Complete);
            match.ResetPlayersLoadedStatus();
            match.InProgress = false;

            matchMembership.Enqueue(match, ServerPacketWriter.MatchComplete(), lobby: false, immune: notPlaying);
            matchMembership.EnqueueState(match);
        }
        finally
        {
            match.Lock.Release();
        }
    }
}
