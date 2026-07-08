using Basil.Application.PacketHandlers.Core;
using Basil.Application.Sessions;
using Basil.Application.UseCases.Multiplayer;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Multiplayer;

/// <summary>Ported from app/api/domains/cho.py's MatchNoBeatmap.</summary>
public sealed class MatchNoBeatmapHandler(MatchMembershipService matchMembership) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.MatchNoBeatmap;

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

            slot.Status = SlotStatus.NoMap;
            matchMembership.EnqueueState(match, false);
        }
        finally
        {
            match.Lock.Release();
        }
    }
}