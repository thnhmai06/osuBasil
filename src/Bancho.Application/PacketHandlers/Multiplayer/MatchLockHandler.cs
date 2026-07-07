using Bancho.Application.Sessions;
using Bancho.Application.UseCases.Multiplayer;
using Bancho.Domain;
using Bancho.Protocol;
using Bancho.Application.PacketHandlers.Core;
using Bancho.Domain.Multiplayer;
using Bancho.Protocol.Packets;

namespace Bancho.Application.PacketHandlers.Multiplayer;

/// <summary>Ported from app/api/domains/cho.py's MatchLock.</summary>
public sealed class MatchLockHandler(MatchMembershipService matchMembership) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.MatchLock;

    public bool AllowedWhenRestricted => false;

    public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var slotId = reader.ReadI32();

        var match = player.Match;
        if (match is null || player.Id != match.HostId || slotId is < 0 or >= 16)
        {
            return;
        }

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
                {
                    // don't allow the host to kick themselves by clicking their own crown.
                    return;
                }

                slot.Status = SlotStatus.Locked;
            }

            matchMembership.EnqueueState(match);
        }
        finally
        {
            match.Lock.Release();
        }
    }
}
