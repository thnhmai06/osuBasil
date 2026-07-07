using Bancho.Application.PacketHandlers.Core;
using Bancho.Application.Sessions;
using Bancho.Application.UseCases.Multiplayer;
using Bancho.Domain.Multiplayer;
using Bancho.Protocol.Packets;

namespace Bancho.Application.PacketHandlers.Multiplayer;

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