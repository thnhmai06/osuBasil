using OpenOsuTournament.Bancho.Application.Sessions;
using OpenOsuTournament.Bancho.Domain;
using OpenOsuTournament.Bancho.Domain.Beatmaps;
using OpenOsuTournament.Bancho.Protocol.Packets;
using Action = OpenOsuTournament.Bancho.Domain.Action;

namespace OpenOsuTournament.Bancho.Application.PacketHandlers.Core;

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

        var mods = reader.ReadU32();
        var mode = reader.ReadU8();

        if ((mods & (uint)Mods.Relax) != 0)
        {
            if (mode == 3) // rx!mania doesn't exist
                mods &= ~(uint)Mods.Relax;
            else
                mode += 4;
        }
        else if ((mods & (uint)Mods.Autopilot) != 0)
        {
            if (mode is 1 or 2 or 3) // ap!catch, taiko and mania don't exist
                mods &= ~(uint)Mods.Autopilot;
            else
                mode += 8;
        }

        var mapId = reader.ReadI32();

        player.Status.Action = (Action)action;
        player.Status.InfoText = infoText;
        player.Status.MapMd5 = mapMd5;
        player.Status.Mods = (Mods)mods;
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