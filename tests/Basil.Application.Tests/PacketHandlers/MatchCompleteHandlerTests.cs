using Basil.Application.PacketHandlers.Multiplayer;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Packets;
using static Basil.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Basil.Application.Tests.PacketHandlers;

/// <summary>
///     Ported from app/api/domains/cho.py's MatchComplete. The is_scrimming scoring branch is dropped along with the
///     rest of the scrim engine.
/// </summary>
public class MatchCompleteHandlerTests
{
    [Fact]
    public async Task Handle_OtherPlayerStillPlaying_DoesNotFinishMatch()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        fixture.RegisterAll(host, guest);
        var match = fixture.CreateMatch(host);
        fixture.MatchMembership.Join(guest, match, "");
        match.Slots[0].Status = SlotStatus.Playing;
        match.Slots[1].Status = SlotStatus.Playing;
        match.InProgress = true;
        host.Dequeue();
        guest.Dequeue();
        var handler = new MatchCompleteHandler(fixture.MatchMembership, fixture.MatchPersistence);

        await handler.HandleAsync(host, new BanchoPacketReader(ReadOnlyMemory<byte>.Empty));

        Assert.Equal(SlotStatus.Complete, match.Slots[0].Status);
        Assert.True(match.InProgress);
        Assert.Empty(host.Dequeue());
    }

    [Fact]
    public async Task Handle_EveryoneDone_FinishesMatchAndBroadcastsCompleteImmuneToNonPlayers()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        var spectatorish = MakePlayer(3, "afk"); // in the room but never played (status stays Open/NotReady)
        fixture.RegisterAll(host, guest, spectatorish);
        var match = fixture.CreateMatch(host);
        fixture.MatchMembership.Join(guest, match, "");
        fixture.MatchMembership.Join(spectatorish, match, "");
        match.Slots[0].Status = SlotStatus.Playing;
        match.Slots[1].Status = SlotStatus.Playing;
        match.Slots[2].Status = SlotStatus.NotReady; // never started playing
        match.InProgress = true;
        match.Slots[1].Loaded = true;
        host.Dequeue();
        guest.Dequeue();
        spectatorish.Dequeue();
        var handler = new MatchCompleteHandler(fixture.MatchMembership, fixture.MatchPersistence);

        await handler.HandleAsync(host, new BanchoPacketReader(ReadOnlyMemory<byte>.Empty));
        await handler.HandleAsync(guest, new BanchoPacketReader(ReadOnlyMemory<byte>.Empty));

        Assert.False(match.InProgress);
        Assert.False(match.Slots[1].Loaded);
        Assert.Contains(ServerPacketWriter.MatchComplete(), Chunk(host.Dequeue()));
        // immune from match_complete itself (still gets the enqueue_state update, just not this packet)
        Assert.DoesNotContain(ServerPacketWriter.MatchComplete(), Chunk(spectatorish.Dequeue()));
    }
}