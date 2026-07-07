using Bancho.Application.Sessions;
using Bancho.Application.UseCases.Multiplayer;
using Bancho.Domain;
using Bancho.Protocol;
using Bancho.Application.PacketHandlers.Core;
using Bancho.Protocol.Packets;

namespace Bancho.Application.PacketHandlers.Multiplayer;

/// <summary>Ported from app/api/domains/cho.py's MatchChangeMods.</summary>
public sealed class MatchChangeModsHandler(MatchMembershipService matchMembership) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.MatchChangeMods;

    public bool AllowedWhenRestricted => false;

    public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var mods = (Mods)reader.ReadI32();

        var match = player.Match;
        if (match is null)
        {
            return;
        }

        await match.Lock.WaitAsync();
        try
        {
            if (match.Freemods)
            {
                if (player.Id == match.HostId)
                {
                    match.Mods = mods & ModsExtensions.SpeedChangingMods;
                }

                var slot = match.GetSlot(player.Id);
                if (slot is null)
                {
                    return;
                }

                slot.Mods = mods & ~ModsExtensions.SpeedChangingMods;
            }
            else
            {
                if (player.Id != match.HostId)
                {
                    return;
                }

                match.Mods = mods;
            }

            matchMembership.EnqueueState(match);
        }
        finally
        {
            match.Lock.Release();
        }
    }
}
