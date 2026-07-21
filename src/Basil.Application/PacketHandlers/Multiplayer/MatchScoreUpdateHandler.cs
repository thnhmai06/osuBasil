using System.Text.Json;
using Basil.Application.PacketHandlers.Core;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Multiplayer;

/// <summary>
///     Ported from app/api/domains/cho.py's MatchScoreUpdate — runs very frequently during a match,
///     so the bancho-protocol relay stays a raw forward (no parsing) exactly like the Python source's
///     "fastpath" comment. The one addition here is decoding the same bytes into a
///     <see cref="Protocol.Multiplayer.ScoreFrameData" /> to publish on the api. host's WS
///     /multi/{id}/{playerName} live channel — a second, independent read of the same buffer, not a
///     change to the relayed packet.
/// </summary>
public sealed class MatchScoreUpdateHandler(MatchMembershipService matchMembership, IMatchEventBus eventBus)
    : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.MatchScoreUpdate;

    public bool AllowedWhenRestricted => false;

    public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var playData = reader.ReadRaw(reader.RemainingLength);

        var match = player.Match;
        if (match is null) return;

        await match.Lock.WaitAsync();
        try
        {
            var slotId = match.GetSlotId(player.Id);
            if (slotId is null) return;

            // scorev2 adds an extra 8 bytes to play_data — either way, byte 11 (4 bytes into the
            // wrapped body) is overwritten with the slot id, matching MatchScoreUpdate.handle.
            var packet = PacketWriter.Wrap(ServerPackets.MatchScoreUpdate, playData);
            packet[11] = (byte)slotId.Value;

            matchMembership.Enqueue(match, packet, false);

            try
            {
                var frame = new BanchoPacketReader(playData).ReadScoreFrame();
                var payload = JsonSerializer.SerializeToUtf8Bytes(
                    MatchLiveSnapshotBuilder.BuildPlayerScore(player.Name, frame));
                eventBus.PublishPlayer(match.DbId, player.Name, payload);
            }
            catch (Exception)
            {
                // ponytail: a malformed/short scoreframe must never break the bancho relay above —
                // the live WS channel just misses this one update.
            }
        }
        finally
        {
            match.Lock.Release();
        }
    }
}