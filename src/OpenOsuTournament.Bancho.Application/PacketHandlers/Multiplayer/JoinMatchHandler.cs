using OpenOsuTournament.Bancho.Application.PacketHandlers.Core;
using OpenOsuTournament.Bancho.Application.Sessions;
using OpenOsuTournament.Bancho.Application.Sessions.Multiplayer;
using OpenOsuTournament.Bancho.Application.UseCases.Multiplayer;
using OpenOsuTournament.Bancho.Protocol.Packets;

namespace OpenOsuTournament.Bancho.Application.PacketHandlers.Multiplayer;

/// <summary>Ported from app/api/domains/cho.py's MatchJoin.</summary>
public sealed class JoinMatchHandler(IMatchRegistry matchRegistry, MatchMembershipService matchMembership)
    : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.JoinMatch;

    public bool AllowedWhenRestricted => false;

    public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var matchId = reader.ReadI32();
        var password = reader.ReadString();

        var match = matchRegistry.GetById(matchId);
        if (match is null)
        {
            player.Enqueue(ServerPacketWriter.MatchJoinFail());
            return;
        }

        if (player.Restricted)
        {
            player.Enqueue([
                .. ServerPacketWriter.MatchJoinFail(),
                .. ServerPacketWriter.Notification("Multiplayer is not available while restricted.")
            ]);
            return;
        }

        if (player.Silenced)
        {
            player.Enqueue([
                .. ServerPacketWriter.MatchJoinFail(),
                .. ServerPacketWriter.Notification("Multiplayer is not available while silenced.")
            ]);
            return;
        }

        await match.Lock.WaitAsync();
        try
        {
            matchMembership.Join(player, match, password);
        }
        finally
        {
            match.Lock.Release();
        }
    }
}