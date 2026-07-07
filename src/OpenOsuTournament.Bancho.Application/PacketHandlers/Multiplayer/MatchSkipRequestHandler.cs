using OpenOsuTournament.Bancho.Application.PacketHandlers.Core;
using OpenOsuTournament.Bancho.Application.Sessions;
using OpenOsuTournament.Bancho.Application.UseCases.Multiplayer;
using OpenOsuTournament.Bancho.Domain.Multiplayer;
using OpenOsuTournament.Bancho.Protocol.Packets;

namespace OpenOsuTournament.Bancho.Application.PacketHandlers.Multiplayer;

/// <summary>Ported from app/api/domains/cho.py's MatchSkipRequest.</summary>
public sealed class MatchSkipRequestHandler(MatchMembershipService matchMembership) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.MatchSkipRequest;

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

            slot.Skipped = true;
            matchMembership.Enqueue(match, ServerPacketWriter.MatchPlayerSkipped(player.Id));

            var everyoneSkipped = match.Slots.All(s => s.Status != SlotStatus.Playing || s.Skipped);
            if (everyoneSkipped) matchMembership.Enqueue(match, ServerPacketWriter.MatchSkip(), false);
        }
        finally
        {
            match.Lock.Release();
        }
    }
}