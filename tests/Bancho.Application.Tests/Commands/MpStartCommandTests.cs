using Bancho.Application.Commands;
using Bancho.Domain;
using static Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Bancho.Application.Tests.Commands;

/// <summary>Ported from app/commands.py's mp_start, minus the delayed-start branches (deferred, see note.md).</summary>
public class MpStartCommandTests
{
    [Fact]
    public async Task HandleAsync_NotAllReady_ReturnsMessageWithoutStarting()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        match.Slots[0].Status = SlotStatus.NotReady;
        var command = new MpStartCommand(fixture.MatchMembership);

        var response = await command.HandleAsync(new MpCommandContext(host, [], match));

        Assert.Equal("Not all players are ready (`!mp start force` to override).", response);
        Assert.False(match.InProgress);
    }

    [Fact]
    public async Task HandleAsync_Force_StartsRegardlessOfReadyState()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        match.Slots[0].Status = SlotStatus.NotReady;
        var command = new MpStartCommand(fixture.MatchMembership);

        var response = await command.HandleAsync(new MpCommandContext(host, ["force"], match));

        Assert.Equal("Good luck!", response);
        Assert.True(match.InProgress);
        Assert.Equal(SlotStatus.Playing, match.Slots[0].Status);
    }

    [Fact]
    public async Task HandleAsync_AllReady_StartsMatch()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        match.Slots[0].Status = SlotStatus.Ready;
        var command = new MpStartCommand(fixture.MatchMembership);

        var response = await command.HandleAsync(new MpCommandContext(host, [], match));

        Assert.Equal("Good luck!", response);
        Assert.True(match.InProgress);
    }

    [Fact]
    public async Task HandleAsync_TooManyArgs_ReturnsInvalidSyntax()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var command = new MpStartCommand(fixture.MatchMembership);

        var response = await command.HandleAsync(new MpCommandContext(host, ["force", "extra"], match));

        Assert.Equal("Invalid syntax: !mp start <force>", response);
    }

    [Fact]
    public async Task HandleAsync_NumericArg_ReturnsNotSupportedMessage()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var command = new MpStartCommand(fixture.MatchMembership);

        var response = await command.HandleAsync(new MpCommandContext(host, ["30"], match));

        Assert.Equal("Delayed match starts aren't supported on this server. Use `!mp start` or `!mp start force`.", response);
        Assert.False(match.InProgress);
    }
}
