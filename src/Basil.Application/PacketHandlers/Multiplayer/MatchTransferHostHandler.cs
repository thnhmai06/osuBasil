using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.PacketHandlers.Core;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Multiplayer;

public sealed class MatchTransferHostHandler(
    IPlayerSessionRegistry sessionRegistry,
    MatchMembershipService matchMembership,
    IMatchPersistenceRepository matchPersistence) : IBanchoPacketHandler
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

            var prevHostId = match.HostId;
            match.HostId = targetId.Value;

            var targetPlayer = sessionRegistry.GetById(targetId.Value);
            targetPlayer?.Enqueue(ServerPacketWriter.MatchTransferHost());
            await matchMembership.EnqueueState(match);

            var prevHostName = sessionRegistry.GetById(prevHostId)?.Name;
            _ = matchPersistence.CreateEventAsync(new MatchEventRow(
                match.DbId, (int)MatchEventType.HostGranted,
                prevHostId, prevHostName, targetId, targetPlayer?.Name,
                DateTimeOffset.UtcNow.UtcDateTime, null));
        }
        finally
        {
            match.Lock.Release();
        }
    }
}
