using Bancho.Application.Sessions;
using Bancho.Application.UseCases.Multiplayer;
using Bancho.Protocol;
using Bancho.Application.PacketHandlers.Core;
using Bancho.Protocol.Packets;

namespace Bancho.Application.PacketHandlers.Multiplayer;

/// <summary>
/// Ported from app/api/domains/cho.py's MatchScoreUpdate — runs very frequently during a match,
/// so it stays a raw forward (no parsing) exactly like the Python source's "fastpath" comment.
/// </summary>
public sealed class MatchScoreUpdateHandler(MatchMembershipService matchMembership) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.MatchScoreUpdate;

    public bool AllowedWhenRestricted => false;

    public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var playData = reader.ReadRaw(reader.RemainingLength);

        var match = player.Match;
        if (match is null)
        {
            return;
        }

        await match.Lock.WaitAsync();
        try
        {
            var slotId = match.GetSlotId(player.Id);
            if (slotId is null)
            {
                return;
            }

            // scorev2 adds an extra 8 bytes to play_data — either way, byte 11 (4 bytes into the
            // wrapped body) is overwritten with the slot id, matching MatchScoreUpdate.handle.
            var packet = PacketWriter.Wrap(ServerPackets.MatchScoreUpdate, playData);
            packet[11] = (byte)slotId.Value;

            matchMembership.Enqueue(match, packet, lobby: false);
        }
        finally
        {
            match.Lock.Release();
        }
    }
}
