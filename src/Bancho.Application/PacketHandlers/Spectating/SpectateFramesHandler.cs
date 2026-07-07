using Bancho.Application.PacketHandlers.Core;
using Bancho.Application.Sessions;
using Bancho.Protocol.Packets;

namespace Bancho.Application.PacketHandlers.Spectating;

/// <summary>
///     Ported from app/api/domains/cho.py's SpectateFrames. Deliberately forwards the raw remaining
///     packet bytes unparsed (matching the Python source's own "fastpath" comment about this packet's
///     sheer send rate) rather than structurally decoding the replay frame bundle.
/// </summary>
public sealed class SpectateFramesHandler : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.SpectateFrames;

    public bool AllowedWhenRestricted => false;

    public Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var rawData = reader.ReadRaw(reader.RemainingLength);
        var packet = ServerPacketWriter.SpectateFrames(rawData);

        foreach (var spectator in player.Spectators) spectator.Enqueue(packet);

        return Task.CompletedTask;
    }
}