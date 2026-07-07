using Bancho.Application.PacketHandlers.Core;
using Bancho.Application.Sessions;
using Bancho.Application.UseCases.Multiplayer;
using Bancho.Protocol.Packets;

namespace Bancho.Application.PacketHandlers.Multiplayer;

/// <summary>
///     Ported from app/api/domains/cho.py's MatchStart. Match.start itself lives in MatchMembershipService.Start,
///     shared with !mp start/force.
/// </summary>
public sealed class MatchStartHandler(MatchMembershipService matchMembership) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.MatchStart;

    public bool AllowedWhenRestricted => false;

    public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var match = player.Match;
        if (match is null || player.Id != match.HostId) return;

        await match.Lock.WaitAsync();
        try
        {
            matchMembership.Start(match);
        }
        finally
        {
            match.Lock.Release();
        }
    }
}