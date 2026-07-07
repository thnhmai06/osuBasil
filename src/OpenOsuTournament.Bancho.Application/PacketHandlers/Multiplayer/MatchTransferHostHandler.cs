using OpenOsuTournament.Bancho.Application.PacketHandlers.Core;
using OpenOsuTournament.Bancho.Application.Sessions;
using OpenOsuTournament.Bancho.Application.UseCases.Multiplayer;
using OpenOsuTournament.Bancho.Protocol.Packets;

namespace OpenOsuTournament.Bancho.Application.PacketHandlers.Multiplayer;

/// <summary>Ported from app/api/domains/cho.py's MatchTransferHost.</summary>
public sealed class MatchTransferHostHandler(
    IPlayerSessionRegistry sessionRegistry,
    MatchMembershipService matchMembership) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.MatchTransferHost;

    public bool AllowedWhenRestricted => false;

    public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var slotId = reader.ReadI32();

        var match = player.Match;
        if (match is null || player.Id != match.HostId || slotId is < 0 or >= 16) return;

        await match.Lock.WaitAsync();
        try
        {
            var targetId = match.Slots[slotId].PlayerId;
            if (targetId is null) return;

            match.HostId = targetId.Value;
            sessionRegistry.GetById(targetId.Value)?.Enqueue(ServerPacketWriter.MatchTransferHost());
            matchMembership.EnqueueState(match);
        }
        finally
        {
            match.Lock.Release();
        }
    }
}