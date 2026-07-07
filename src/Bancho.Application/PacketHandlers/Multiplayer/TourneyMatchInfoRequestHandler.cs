using Bancho.Application.Sessions;
using Bancho.Application.UseCases.Multiplayer;
using Bancho.Domain;
using Bancho.Protocol;
using Bancho.Application.PacketHandlers.Core;
using Bancho.Application.Sessions.Multiplayer;
using Bancho.Domain.Users;
using Bancho.Protocol.Packets;

namespace Bancho.Application.PacketHandlers.Multiplayer;

/// <summary>Ported from app/api/domains/cho.py's TourneyMatchInfoRequest.</summary>
public sealed class TourneyMatchInfoRequestHandler(IMatchRegistry matchRegistry) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.TournamentMatchInfoRequest;

    public bool AllowedWhenRestricted => false;

    public Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var matchId = reader.ReadI32();

        if (matchId is < 0 or >= 64 || (player.Priv & Privileges.Donator) == 0)
        {
            return Task.CompletedTask;
        }

        var match = matchRegistry.GetById(matchId);
        if (match is null)
        {
            return Task.CompletedTask;
        }

        player.Enqueue(ServerPacketWriter.UpdateMatch(MatchPacketDataMapper.ToPacketData(match), sendPassword: false));
        return Task.CompletedTask;
    }
}
