using Bancho.Application.Sessions;
using Bancho.Application.UseCases.Multiplayer;
using Bancho.Domain;
using Bancho.Protocol;
using Bancho.Application.PacketHandlers.Core;
using Bancho.Domain.Multiplayer;
using Bancho.Protocol.Packets;

namespace Bancho.Application.PacketHandlers.Multiplayer;

/// <summary>
/// Ported from app/api/domains/cho.py's MatchComplete. The `is_scrimming` branch
/// (asyncio.create_task(update_matchpoints(was_playing))) is dropped along with the rest of the
/// scrim engine — see note.md.
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
