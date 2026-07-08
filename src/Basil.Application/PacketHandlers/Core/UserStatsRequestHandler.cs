using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Core;

/// <summary>Ported from app/api/domains/cho.py's StatsRequest.</summary>
public sealed class UserStatsRequestHandler(IPlayerSessionRegistry sessionRegistry) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.UserStatsRequest;

    public bool AllowedWhenRestricted => true;

    public Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var requestedIds = reader.ReadI32ListI16L();
        var unrestrictedIds = sessionRegistry.All.Where(s => !s.Restricted).Select(s => s.Id).ToHashSet();

        foreach (var id in requestedIds)
        {
            if (id == player.Id || !unrestrictedIds.Contains(id)) continue;

            var target = sessionRegistry.GetById(id);
            if (target is not null) player.Enqueue(PacketBuilders.BuildUserStats(target));
        }

        return Task.CompletedTask;
    }
}