using Basil.Application.Sessions;
using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Core;

/// <summary>Ported from app/api/domains/cho.py's ChangeAction.</summary>
public sealed class ChangeActionHandler(IPlayerSessionRegistry sessionRegistry) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.ChangeAction;

    public bool AllowedWhenRestricted => true;

    public Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var action = reader.ReadU8();
        var infoText = reader.ReadString();
        var mapMd5 = reader.ReadString();

        var mods = (Mods)reader.ReadU32();
        var mode = reader.ReadU8();
        var mapId = reader.ReadI32();

        player.Status.UserActivity = (UserActivity)action;
        player.Status.InfoText = infoText;
        player.Status.MapMd5 = mapMd5;
        player.Status.Mods = mods;
        player.Status.Mode = (GameMode)mode;
        player.Status.MapId = mapId;

        if (!player.Restricted)
        {
            var statsPacket = PacketBuilders.BuildUserStats(player);
            foreach (var other in sessionRegistry.All) other.Enqueue(statsPacket);
        }

        return Task.CompletedTask;
    }
}