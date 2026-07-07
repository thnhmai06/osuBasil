using Bancho.Application.Sessions;
using Bancho.Application.UseCases.Multiplayer;
using Bancho.Domain;
using Bancho.Protocol;
using Bancho.Application.PacketHandlers.Core;
using Bancho.Domain.Multiplayer;
using Bancho.Protocol.Packets;

namespace Bancho.Application.PacketHandlers.Multiplayer;

/// <summary>Ported from app/api/domains/cho.py's MatchLoadComplete.</summary>
public sealed class MatchLoadCompleteHandler(MatchMembershipService matchMembership) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.MatchLoadComplete;

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

            slot.Loaded = true;

            var stillWaiting = match.Slots.Any(s => s.Status == SlotStatus.Playing && !s.Loaded);
            if (!stillWaiting)
            {
                matchMembership.Enqueue(match, ServerPacketWriter.MatchAllPlayersLoaded(), lobby: false);
            }
        }
        finally
        {
            match.Lock.Release();
        }
    }
}
