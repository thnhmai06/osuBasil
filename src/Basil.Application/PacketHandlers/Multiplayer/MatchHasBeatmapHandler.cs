using Basil.Application.PacketHandlers.Core;
using Basil.Application.Sessions;
using Basil.Application.UseCases.Multiplayer;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Multiplayer;

/// <summary>Ported from app/api/domains/cho.py's MatchHasBeatmap.</summary>
public sealed class MatchHasBeatmapHandler(MatchMembershipService matchMembership) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.MatchHasBeatmap;

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

            slot.Status = SlotStatus.NotReady;
            matchMembership.EnqueueState(match, false);
        }
        finally
        {
            match.Lock.Release();
        }
    }
}