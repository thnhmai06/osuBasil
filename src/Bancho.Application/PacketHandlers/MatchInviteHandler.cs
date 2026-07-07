using Bancho.Application.Sessions;
using Bancho.Protocol;

namespace Bancho.Application.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's MatchInvite. The bot-target special case is dropped along with BanchoBot.</summary>
public sealed class MatchInviteHandler(IPlayerSessionRegistry sessionRegistry) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.MatchInvite;

    public bool AllowedWhenRestricted => false;

    public Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var userId = reader.ReadI32();

        var match = player.Match;
        if (match is null)
        {
            return Task.CompletedTask;
        }

        var target = sessionRegistry.GetById(userId);
        if (target is null)
        {
            return Task.CompletedTask;
        }

        target.Enqueue(ServerPacketWriter.MatchInvite(player.Id, player.Name, match.Embed, target.Name));
        return Task.CompletedTask;
    }
}
