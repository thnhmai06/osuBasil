using Bancho.Application.Commands;
using Bancho.Domain;
using static Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Bancho.Application.Tests.Commands;

/// <summary>Ported from app/commands.py's mp_freemods.</summary>
public class MpFreemodsCommandTests
{
    [Fact]
    public async Task HandleAsync_InvalidArg_ReturnsUsage()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var command = new MpFreemodsCommand(fixture.MatchMembership);

        var response = await command.HandleAsync(new MpCommandContext(host, ["maybe"], match));

        Assert.Equal("Invalid syntax: !mp freemods <on/off>", response);
    }

    [Fact]
    public async Task HandleAsync_On_PushesNonSpeedMatchModsToSlotsAndKeepsSpeedModsCentral()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        match.Mods = Mods.DoubleTime | Mods.Hidden;
        var command = new MpFreemodsCommand(fixture.MatchMembership);

        var response = await command.HandleAsync(new MpCommandContext(host, ["on"], match));

        Assert.Equal("Match freemod status updated.", response);
        Assert.True(match.Freemods);
        Assert.Equal(Mods.DoubleTime, match.Mods);
        Assert.Equal(Mods.Hidden, match.Slots[0].Mods);
    }

    [Fact]
    public async Task HandleAsync_Off_CollapsesSlotModsIntoMatchModsAndResetsSlots()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        match.Freemods = true;
        match.Mods = Mods.DoubleTime;
        match.Slots[0].Mods = Mods.Hidden;
        var command = new MpFreemodsCommand(fixture.MatchMembership);

        var response = await command.HandleAsync(new MpCommandContext(host, ["off"], match));

        Assert.Equal("Match freemod status updated.", response);
        Assert.False(match.Freemods);
        Assert.Equal(Mods.DoubleTime | Mods.Hidden, match.Mods);
        Assert.Equal(Mods.NoMod, match.Slots[0].Mods);
    }
}
