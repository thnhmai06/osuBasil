using Bancho.Application.Sessions;
using Bancho.Protocol;

namespace Bancho.Application.PacketHandlers;

/// <summary>
/// Ported from app/api/domains/cho.py's UserPresenceRequestAll — only sent by the client when
/// more than 256 players are visible.
/// </summary>
public sealed class UserPresenceRequestAllHandler(IPlayerSessionRegistry sessionRegistry) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.UserPresenceRequestAll;

    public bool AllowedWhenRestricted => false;

    public Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        reader.ReadI32(); // ingame_time, unused

        var buffer = new List<byte>();
        foreach (var other in sessionRegistry.All.Where(s => !s.Restricted))
        {
            buffer.AddRange(PacketBuilders.BuildUserPresence(other));
        }

        player.Enqueue([.. buffer]);
        return Task.CompletedTask;
    }
}
