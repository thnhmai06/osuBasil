using Bancho.Application.Sessions;
using Bancho.Application.UseCases.Multiplayer;
using Bancho.Domain;
using Bancho.Protocol;

namespace Bancho.Application.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's MatchStart, inlining Match.start.</summary>
public sealed class MatchStartHandler(MatchMembershipService matchMembership) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.MatchStart;

    public bool AllowedWhenRestricted => false;

    public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var match = player.Match;
        if (match is null || player.Id != match.HostId)
        {
            return;
        }

        await match.Lock.WaitAsync();
        try
        {
            var noMap = new List<int>();
            foreach (var slot in match.Slots)
            {
                if (slot.PlayerId is not null)
                {
                    if (slot.Status != SlotStatus.NoMap)
                    {
                        slot.Status = SlotStatus.Playing;
                    }
                    else
                    {
                        noMap.Add(slot.PlayerId.Value);
                    }
                }
            }

            match.InProgress = true;
            matchMembership.Enqueue(match, ServerPacketWriter.MatchStart(MatchPacketDataMapper.ToPacketData(match)), lobby: false, immune: noMap);
            matchMembership.EnqueueState(match);
        }
        finally
        {
            match.Lock.Release();
        }
    }
}
