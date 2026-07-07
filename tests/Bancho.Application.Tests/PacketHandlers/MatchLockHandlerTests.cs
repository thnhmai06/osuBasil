using Bancho.Application.PacketHandlers.Multiplayer;
using Bancho.Domain.Multiplayer;
using Bancho.Protocol.Packets;
using static Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Bancho.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's MatchLock.</summary>
public class MatchLockHandlerTests
{
    private static BanchoPacketReader ReaderFor(int slotId)
    {
        return new BanchoPacketReader(PacketWriter.WriteInt32(slotId));
    }

    [Fact]
    public async Task Handle_NonHost_NoOp()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        fixture.RegisterAll(host, guest);
        var match = fixture.CreateMatch(host);
        fixture.MatchMembership.Join(guest, match, "");
        var handler = new MatchLockHandler(fixture.MatchMembership);

        await handler.HandleAsync(guest, ReaderFor(3));

        Assert.Equal(SlotStatus.Open, match.Slots[3].Status);
    }

    [Fact]
    public async Task Handle_HostLocksOpenSlot_BecomesLocked()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var handler = new MatchLockHandler(fixture.MatchMembership);

        await handler.HandleAsync(host, ReaderFor(3));

        Assert.Equal(SlotStatus.Locked, match.Slots[3].Status);
    }

    [Fact]
    public async Task Handle_HostTogglesLockedSlot_BecomesOpenAgain()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        match.Slots[3].Status = SlotStatus.Locked;
        var handler = new MatchLockHandler(fixture.MatchMembership);

        await handler.HandleAsync(host, ReaderFor(3));

        Assert.Equal(SlotStatus.Open, match.Slots[3].Status);
    }

    [Fact]
    public async Task Handle_HostClicksOwnCrown_DoesNotKickSelf()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var handler = new MatchLockHandler(fixture.MatchMembership);

        await handler.HandleAsync(host, ReaderFor(0));

        Assert.Equal(SlotStatus.NotReady, match.Slots[0].Status);
        Assert.Equal(host.Id, match.Slots[0].PlayerId);
    }
}