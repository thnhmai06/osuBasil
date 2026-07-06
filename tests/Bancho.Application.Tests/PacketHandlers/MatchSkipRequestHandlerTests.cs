using Bancho.Application.PacketHandlers;
using Bancho.Domain;
using Bancho.Protocol;
using static Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Bancho.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's MatchSkipRequest.</summary>
public class MatchSkipRequestHandlerTests
{
    [Fact]
    public async Task Handle_NotEveryonePlayingHasSkipped_DoesNotBroadcastSkip()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        fixture.RegisterAll(host, guest);
        var match = fixture.CreateMatch(host);
        fixture.MatchMembership.Join(guest, match, "");
        match.Slots[0].Status = SlotStatus.Playing;
        match.Slots[1].Status = SlotStatus.Playing;
        host.Dequeue();
        guest.Dequeue();
        var handler = new MatchSkipRequestHandler(fixture.MatchMembership);

        await handler.HandleAsync(host, new BanchoPacketReader(ReadOnlyMemory<byte>.Empty));

        Assert.True(match.Slots[0].Skipped);
        Assert.Contains(ServerPacketWriter.MatchPlayerSkipped(host.Id), Chunk(host.Dequeue()));
        Assert.DoesNotContain(ServerPacketWriter.MatchSkip(), Chunk(guest.Dequeue()));
    }

    [Fact]
    public async Task Handle_EveryonePlayingHasSkipped_BroadcastsSkip()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        fixture.RegisterAll(host, guest);
        var match = fixture.CreateMatch(host);
        fixture.MatchMembership.Join(guest, match, "");
        match.Slots[0].Status = SlotStatus.Playing;
        match.Slots[1].Status = SlotStatus.Playing;
        match.Slots[1].Skipped = true;
        host.Dequeue();
        guest.Dequeue();
        var handler = new MatchSkipRequestHandler(fixture.MatchMembership);

        await handler.HandleAsync(host, new BanchoPacketReader(ReadOnlyMemory<byte>.Empty));

        Assert.Contains(ServerPacketWriter.MatchSkip(), Chunk(host.Dequeue()));
    }
}
