using OpenOsuTournament.Bancho.Application.PacketHandlers.Core;
using OpenOsuTournament.Bancho.Application.Sessions;
using OpenOsuTournament.Bancho.Application.UseCases.Multiplayer;
using OpenOsuTournament.Bancho.Protocol.Packets;

namespace OpenOsuTournament.Bancho.Application.PacketHandlers.Multiplayer;

/// <summary>Ported from app/api/domains/cho.py's MatchCreate.</summary>
public sealed class CreateMatchHandler(MatchMembershipService matchMembership) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.CreateMatch;

    public bool AllowedWhenRestricted => false;

    public Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var matchData = reader.ReadMatch();

        if (!MatchMembershipService.ValidateMatchData(matchData, player.Id)) return Task.CompletedTask;

        if (player.Restricted)
        {
            player.Enqueue([
                .. ServerPacketWriter.MatchJoinFail(),
                .. ServerPacketWriter.Notification("Multiplayer is not available while restricted.")
            ]);
            return Task.CompletedTask;
        }

        if (player.Silenced)
        {
            player.Enqueue([
                .. ServerPacketWriter.MatchJoinFail(),
                .. ServerPacketWriter.Notification("Multiplayer is not available while silenced.")
            ]);
            return Task.CompletedTask;
        }

        var match = matchMembership.Create(player, matchData);
        if (match is null) player.Enqueue(ServerPacketWriter.MatchJoinFail());

        return Task.CompletedTask;
    }
}