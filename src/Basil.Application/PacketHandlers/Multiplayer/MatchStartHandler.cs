using Basil.Application.PacketHandlers.Core;
using Basil.Application.Sessions;
using Basil.Application.UseCases.Multiplayer;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Multiplayer;

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
            await matchMembership.StartAsync(match);
        }
        finally
        {
            match.Lock.Release();
        }
    }
}