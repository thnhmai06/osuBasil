using Bancho.Application.Sessions;
using Bancho.Application.UseCases.Multiplayer;
using Bancho.Domain;
using Bancho.Protocol;

namespace Bancho.Application.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's MatchSkipRequest.</summary>
public sealed class MatchSkipRequestHandler(MatchMembershipService matchMembership) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.MatchSkipRequest;

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

            slot.Skipped = true;
            matchMembership.Enqueue(match, ServerPacketWriter.MatchPlayerSkipped(player.Id));

            var everyoneSkipped = match.Slots.All(s => s.Status != SlotStatus.Playing || s.Skipped);
            if (everyoneSkipped)
            {
                matchMembership.Enqueue(match, ServerPacketWriter.MatchSkip(), lobby: false);
            }
        }
        finally
        {
            match.Lock.Release();
        }
    }
}
