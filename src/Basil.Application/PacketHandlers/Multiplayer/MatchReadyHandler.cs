using Basil.Application.PacketHandlers.Core;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Multiplayer;

/// <summary>Ported from app/api/domains/cho.py's MatchReady.</summary>
public sealed class MatchReadyHandler(MatchMembershipService matchMembership) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.MatchReady;

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

            slot.Status = SlotStatus.Ready;
            matchMembership.EnqueueState(match, false);
        }
        finally
        {
            match.Lock.Release();
        }
    }
}