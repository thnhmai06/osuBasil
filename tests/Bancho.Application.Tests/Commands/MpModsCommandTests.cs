using Bancho.Application.Commands;
using Bancho.Domain;
using static Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Bancho.Application.Tests.Commands;

/// <summary>Ported from app/commands.py's mp_mods.</summary>
public class MpModsCommandTests
{
    [Fact]
    public async Task HandleAsync_OddLengthModString_ReturnsInvalidSyntax()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var command = new MpModsCommand(fixture.MatchMembership);

        var response = await command.HandleAsync(new MpCommandContext(host, ["HDD"], match));

        Assert.Equal("Invalid syntax: !mp mods <mods>", response);
    }

    [Fact]
    public async Task HandleAsync_NotFreemods_SetsMatchMods()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var command = new MpModsCommand(fixture.MatchMembership);

        var response = await command.HandleAsync(new MpCommandContext(host, ["HD"], match));

        Assert.Equal("Match mods updated.", response);
        Assert.Equal(Mods.Hidden, match.Mods);
    }

    [Fact]
    public async Task HandleAsync_Freemods_HostSetsSpeedChangingMatchModsPlusOwnSlotMods()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        match.Freemods = true;
        var command = new MpModsCommand(fixture.MatchMembership);

        var response = await command.HandleAsync(new MpCommandContext(host, ["DTHD"], match));

        Assert.Equal("Match mods updated.", response);
        Assert.Equal(Mods.DoubleTime, match.Mods);
        Assert.Equal(Mods.Hidden, match.Slots[0].Mods);
    }

    [Fact]
    public async Task HandleAsync_Freemods_NonHostOnlySetsOwnSlotMods()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        fixture.RegisterAll(host, guest);
        var match = fixture.CreateMatch(host);
        fixture.MatchMembership.Join(guest, match, "");
        match.Freemods = true;
        match.Mods = Mods.NoMod;
        var command = new MpModsCommand(fixture.MatchMembership);

        var response = await command.HandleAsync(new MpCommandContext(guest, ["HD"], match));

        Assert.Equal("Match mods updated.", response);
        Assert.Equal(Mods.NoMod, match.Mods);
        Assert.Equal(Mods.Hidden, match.Slots[1].Mods);
    }
}
