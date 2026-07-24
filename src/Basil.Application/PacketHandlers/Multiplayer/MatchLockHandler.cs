using Basil.Application.PacketHandlers.Core;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Multiplayer;

/// <summary>Ported from app/api/domains/cho.py's MatchLock.</summary>
public sealed class MatchLockHandler(MatchMembershipService matchMembership) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.MatchLock;

    public bool AllowedWhenRestricted => false;

    public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var slotId = reader.ReadI32();

        var match = player.Match;
        if (match is null || player.Id != match.HostId || slotId is < 0 or >= 16) return;

        await match.Lock.WaitAsync();
        try
        {
            var slot = match.Slots[slotId];

            if (slot.Status == SlotStatus.Locked)
            {
                slot.Status = SlotStatus.Open;
            }
            else
            {
                if (slot.PlayerId == player.Id)
                    // don't allow the host to kick themselves by clicking their own crown.
                    return;

                slot.Status = SlotStatus.Locked;
            }

            await matchMembership.EnqueueState(match);
        }
        finally
        {
            match.Lock.Release();
        }
    }
}