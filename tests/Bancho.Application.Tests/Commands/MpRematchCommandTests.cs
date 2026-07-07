using Bancho.Application.Commands;
using Bancho.Application.Sessions;
using static Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Bancho.Application.Tests.Commands;

/// <summary>Ported from app/commands.py's mp_rematch.</summary>
public class MpRematchCommandTests
{
    [Fact]
    public async Task HandleAsync_WithArgs_ReturnsInvalidSyntax()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var command = new MpRematchCommand(fixture.SessionRegistry);

        var response = await command.HandleAsync(new MpCommandContext(host, ["extra"], match));

        Assert.Equal("Invalid syntax: !mp rematch", response);
    }

    [Fact]
    public async Task HandleAsync_NotHost_ReturnsMessage()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        fixture.RegisterAll(host, guest);
        var match = fixture.CreateMatch(host);
        fixture.MatchMembership.Join(guest, match, "");
        var command = new MpRematchCommand(fixture.SessionRegistry);

        var response = await command.HandleAsync(new MpCommandContext(guest, [], match));

        Assert.Equal("Only available to the host.", response);
    }

    [Fact]
    public async Task HandleAsync_NoPreviousScrim_ReturnsMessage()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var command = new MpRematchCommand(fixture.SessionRegistry);

        var response = await command.HandleAsync(new MpCommandContext(host, [], match));

        Assert.Equal("No scrim to rematch; to start one, use !mp scrim.", response);
    }

    [Fact]
    public async Task HandleAsync_FinishedScrim_RestartsWithSameWinningPoints()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        match.WinningPoints = 3;
        var command = new MpRematchCommand(fixture.SessionRegistry);

        var response = await command.HandleAsync(new MpCommandContext(host, [], match));

        Assert.Equal("A rematch has been started by host; first to 3 points wins. Best of luck!", response);
        Assert.True(match.IsScrimming);
    }

    [Fact]
    public async Task HandleAsync_NoPointsAwardedYet_ReturnsMessage()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        match.IsScrimming = true;
        var command = new MpRematchCommand(fixture.SessionRegistry);

        var response = await command.HandleAsync(new MpCommandContext(host, [], match));

        Assert.Equal("No match points have yet been awarded!", response);
    }

    [Fact]
    public async Task HandleAsync_LastPointWasATie_ReturnsMessage()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        match.IsScrimming = true;
        match.RecordWinner(null);
        var command = new MpRematchCommand(fixture.SessionRegistry);

        var response = await command.HandleAsync(new MpCommandContext(host, [], match));

        Assert.Equal("The last point was a tie!", response);
    }

    [Fact]
    public async Task HandleAsync_LastPointHadAWinner_DeductsPointAndPopsWinner()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        match.IsScrimming = true;
        var winner = ScrimParticipant.OfPlayer(host.Id);
        match.AddMatchPoint(winner);
        match.RecordWinner(winner);
        var command = new MpRematchCommand(fixture.SessionRegistry);

        var response = await command.HandleAsync(new MpCommandContext(host, [], match));

        Assert.Equal("A point has been deducted from host.", response);
        Assert.Equal(0, match.GetMatchPoints(winner));
        Assert.Empty(match.Winners);
    }
}
