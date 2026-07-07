using Bancho.Application.Commands;
using static Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Bancho.Application.Tests.Commands;

/// <summary>Ported from app/commands.py's mp_addref.</summary>
public class MpAddRefCommandTests
{
    [Fact]
    public async Task HandleAsync_TargetNotInMatch_ReturnsMessage()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var outsider = MakePlayer(2, "outsider");
        fixture.RegisterAll(host, outsider);
        var match = fixture.CreateMatch(host);
        var command = new MpAddRefCommand(fixture.SessionRegistry);

        var response = await command.HandleAsync(new MpCommandContext(host, ["outsider"], match));

        Assert.Equal("User must be in the current match!", response);
    }

    [Fact]
    public async Task HandleAsync_AlreadyReferee_ReturnsMessage()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var command = new MpAddRefCommand(fixture.SessionRegistry);

        var response = await command.HandleAsync(new MpCommandContext(host, ["host"], match));

        Assert.Equal("host is already a match referee!", response);
    }

    [Fact]
    public async Task HandleAsync_TargetInMatch_AddsReferee()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        fixture.RegisterAll(host, guest);
        var match = fixture.CreateMatch(host);
        fixture.MatchMembership.Join(guest, match, "");
        var command = new MpAddRefCommand(fixture.SessionRegistry);

        var response = await command.HandleAsync(new MpCommandContext(host, ["guest"], match));

        Assert.Equal("guest added to match referees.", response);
        Assert.True(match.IsReferee(guest.Id));
    }
}
