using Basil.Application.PacketHandlers.Core;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Multiplayer;

/// <summary>Ported from app/api/domains/cho.py's MatchChangeSlot.</summary>
public sealed class MatchChangeSlotHandler(MatchMembershipService matchMembership) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.MatchChangeSlot;

    public bool AllowedWhenRestricted => false;

    public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var slotId = reader.ReadI32();

        var match = player.Match;
        if (match is null || slotId is < 0 or >= 16) return;

        await match.Lock.WaitAsync();
        try
        {
            if (match.Slots[slotId].Status != SlotStatus.Open) return;

            var slot = match.GetSlot(player.Id);
            if (slot is null) return;

            match.Slots[slotId].CopyFrom(slot);
            slot.Reset();

            matchMembership.EnqueueState(match);
        }
        finally
        {
            match.Lock.Release();
        }
    }
}