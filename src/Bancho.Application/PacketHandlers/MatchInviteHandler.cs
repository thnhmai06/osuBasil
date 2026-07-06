using Bancho.Application.Sessions;
using Bancho.Protocol;

namespace Bancho.Application.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's MatchInvite.</summary>
public sealed class MatchInviteHandler(IPlayerSessionRegistry sessionRegistry) : IBanchoPacketHandler
{
    private const string BotName = "BanchoBot";

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

        if (target.IsBotClient)
        {
            player.Enqueue(ServerPacketWriter.SendMessage(BotName, "I'm too busy!", player.Name, target.Id));
            return Task.CompletedTask;
        }

        target.Enqueue(ServerPacketWriter.MatchInvite(player.Id, player.Name, match.Embed, target.Name));
        return Task.CompletedTask;
    }
}
