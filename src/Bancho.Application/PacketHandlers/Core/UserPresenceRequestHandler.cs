using Bancho.Application.Sessions;
using Bancho.Protocol;
using Bancho.Protocol.Packets;

namespace Bancho.Application.PacketHandlers.Core;

/// <summary>Ported from app/api/domains/cho.py's UserPresenceRequest.</summary>
public sealed class UserPresenceRequestHandler(IPlayerSessionRegistry sessionRegistry) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.UserPresenceRequest;

    public bool AllowedWhenRestricted => false;

    public Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        foreach (var id in reader.ReadI32ListI16L())
        {
            var target = sessionRegistry.GetById(id);
            if (target is not null)
            {
                player.Enqueue(PacketBuilders.BuildUserPresence(target));
            }
        }

        return Task.CompletedTask;
    }
}
