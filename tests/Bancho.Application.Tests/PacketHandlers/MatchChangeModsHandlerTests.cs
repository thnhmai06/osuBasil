using Bancho.Application.PacketHandlers.Multiplayer;
using Bancho.Domain;
using Bancho.Protocol.Packets;
using static Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Bancho.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's MatchChangeMods.</summary>
public class MatchChangeModsHandlerTests
{
    private static BanchoPacketReader ReaderFor(Mods mods)
    {
        return new BanchoPacketReader(PacketWriter.WriteInt32((int)mods));
    }

    [Fact]
    public async Task Handle_NotFreemods_NonHost_NoOp()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        fixture.RegisterAll(host, guest);
        var match = fixture.CreateMatch(host);
        fixture.MatchMembership.Join(guest, match, "");
        var handler = new MatchChangeModsHandler(fixture.MatchMembership);

        await handler.HandleAsync(guest, ReaderFor(Mods.Hidden));

        Assert.Equal(Mods.NoMod, match.Mods);
    }

    [Fact]
    public async Task Handle_NotFreemods_Host_SetsMatchMods()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var handler = new MatchChangeModsHandler(fixture.MatchMembership);

        await handler.HandleAsync(host, ReaderFor(Mods.Hidden | Mods.DoubleTime));

        Assert.Equal(Mods.Hidden | Mods.DoubleTime, match.Mods);
    }

    [Fact]
    public async Task Handle_Freemods_NonHost_OnlySetsOwnSlotNonSpeedMods()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        fixture.RegisterAll(host, guest);
        var match = fixture.CreateMatch(host);
        fixture.MatchMembership.Join(guest, match, "");
        match.Freemods = true;
        var handler = new MatchChangeModsHandler(fixture.MatchMembership);

        await handler.HandleAsync(guest, ReaderFor(Mods.Hidden | Mods.DoubleTime));

        Assert.Equal(Mods.Hidden, match.GetSlot(guest.Id)!.Mods);
        Assert.Equal(Mods.NoMod, match.Mods); // DT (speed-changing) ignored from a non-host
    }

    [Fact]
    public async Task Handle_Freemods_Host_SetsSpeedChangingMatchModsAndOwnSlotMods()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        match.Freemods = true;
        var handler = new MatchChangeModsHandler(fixture.MatchMembership);

        await handler.HandleAsync(host, ReaderFor(Mods.Hidden | Mods.DoubleTime));

        Assert.Equal(Mods.DoubleTime, match.Mods);
        Assert.Equal(Mods.Hidden, match.GetSlot(host.Id)!.Mods);
    }
}