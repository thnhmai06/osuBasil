using System.Text.Json;
using Basil.Application.PacketHandlers.Core;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Application.UseCases.Multiplayer;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Spectating;

/// <summary>
///     Ported from app/api/domains/cho.py's SpectateFrames. Deliberately forwards the raw remaining
///     packet bytes unparsed (matching the Python source's own "fastpath" comment about this packet's
///     sheer send rate) rather than structurally decoding the replay frame bundle. When the spectated
///     player is in a multiplayer match, the same raw bytes are also published (base64-wrapped) on
///     the api. host's WS /multi/{id}/input channel — new for that WS layer, not part of the ported
///     bancho relay above.
/// </summary>
public sealed class SpectateFramesHandler(IMatchEventBus eventBus) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.SpectateFrames;

    public bool AllowedWhenRestricted => false;

    public Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var rawData = reader.ReadRaw(reader.RemainingLength);
        var packet = ServerPacketWriter.SpectateFrames(rawData);

        foreach (var spectator in player.Spectators) spectator.Enqueue(packet);

        if (player.Match is { } match)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(
                new PlayerInputFrame(player.Name, Convert.ToBase64String(rawData)));
            eventBus.PublishInput(match.DbId, payload);
        }

        return Task.CompletedTask;
    }
}