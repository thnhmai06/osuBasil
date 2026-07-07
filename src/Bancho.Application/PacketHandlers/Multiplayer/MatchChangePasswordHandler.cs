using Bancho.Application.Sessions;
using Bancho.Application.UseCases.Multiplayer;
using Bancho.Protocol;
using Bancho.Application.PacketHandlers.Core;
using Bancho.Protocol.Packets;

namespace Bancho.Application.PacketHandlers.Multiplayer;

/// <summary>Ported from app/api/domains/cho.py's MatchChangePassword.</summary>
public sealed class MatchChangePasswordHandler(MatchMembershipService matchMembership) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.MatchChangePassword;

    public bool AllowedWhenRestricted => false;

    public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var matchData = reader.ReadMatch();

        var match = player.Match;
        if (!MatchMembershipService.ValidateMatchData(matchData, player.Id) || match is null || player.Id != match.HostId)
        {
            return;
        }

        await match.Lock.WaitAsync();
        try
        {
            match.Password = matchData.Password;
            matchMembership.EnqueueState(match);
        }
        finally
        {
            match.Lock.Release();
        }
    }
}
