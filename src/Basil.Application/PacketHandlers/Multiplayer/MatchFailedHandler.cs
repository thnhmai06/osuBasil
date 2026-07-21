using Basil.Application.PacketHandlers.Core;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Multiplayer;

/// <summary>Ported from app/api/domains/cho.py's MatchFailed.</summary>
public sealed class MatchFailedHandler(MatchMembershipService matchMembership) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.MatchFailed;

    public bool AllowedWhenRestricted => false;

    public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var match = player.Match;
        if (match is null) return;

        await match.Lock.WaitAsync();
        try
        {
            var slotId = match.GetSlotId(player.Id);
            if (slotId is null) return;

            matchMembership.Enqueue(match, ServerPacketWriter.MatchPlayerFailed(slotId.Value), false);
        }
        finally
        {
            match.Lock.Release();
        }
    }
}