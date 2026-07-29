using System.Text.Json;
using Basil.Application.Json;
using Basil.Application.PacketHandlers.Core;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Services.Spectating;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Spectating;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Spectating;

/// <summary>
///     Ported from app/api/domains/cho.py's SpectateFrames. The bancho-protocol relay to native
///     spectators stays a raw forward of the packet's remaining bytes (matching the Python source's
///     own "fastpath" comment about this packet's sheer send rate) — no parsing on that path. The
///     same raw bytes are also decoded into a structured <see cref="SpectateFramesEvent" /> (a second,
///     independent read of the same buffer, not a change to the relayed packet — same pattern as
///     <see cref="Multiplayer.MatchScoreUpdateHandler" />'s scoreframe decode) and published on the
///     api. host's SSE `/users/{idOrName}/live` channel, keyed by this player's id regardless of
///     match membership.
/// </summary>
public sealed class SpectateFramesHandler(IPlayerInputEvents playerInputEvents) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.SpectateFrames;

    public bool AllowedWhenRestricted => false;

    public Task HandleAsync(PlayerSession player, BanchoPacketReader reader,
        CancellationToken cancellationToken = default)
    {
        var rawData = reader.ReadRaw(reader.RemainingLength);
        var packet = ServerPacketWriter.SpectateFrames(rawData);

        foreach (var spectator in player.Spectators) spectator.Enqueue(packet);

        if (playerInputEvents.HasSubscribers)
            try
            {
                var bundle = new BanchoPacketReader(rawData).ReadReplayFrameBundle();
                var user = new UserBrief(player.Id, player.Name, player.Geoloc.Country);
                var payload = JsonSerializer.SerializeToUtf8Bytes(
                    new SpectateFramesEvent(user, bundle.Action, bundle.ExtraByte, bundle.Frames, bundle.ScoreFrame),
                    BasilJsonOptions.Instance);
                playerInputEvents.PublishInput(player.Id, payload);
            }
            catch (Exception)
            {
                // A malformed/short bundle must never break the bancho relay above — the SSE channel
                // just misses this one update.
            }

        return Task.CompletedTask;
    }
}