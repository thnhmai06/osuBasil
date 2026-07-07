using Bancho.Application.Commands;
using static Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Bancho.Application.Tests.Commands;

/// <summary>Ported from app/commands.py's mp_rmref.</summary>
public class MpRmRefCommandTests
{
    [Fact]
    public async Task HandleAsync_NotAReferee_ReturnsMessage()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        fixture.RegisterAll(host, guest);
        var match = fixture.CreateMatch(host);
        fixture.MatchMembership.Join(guest, match, "");
        var command = new MpRmRefCommand(fixture.SessionRegistry);

        var response = await command.HandleAsync(new MpCommandContext(host, ["guest"], match));

        Assert.Equal("guest is not a match referee!", response);
    }

    [Fact]
    public async Task HandleAsync_TargetIsHost_ReturnsMessage()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var command = new MpRmRefCommand(fixture.SessionRegistry);

        var response = await command.HandleAsync(new MpCommandContext(host, ["host"], match));

        Assert.Equal("The host is always a referee!", response);
    }

    [Fact]
    public async Task HandleAsync_AddedReferee_RemovesReferee()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        fixture.RegisterAll(host, guest);
        var match = fixture.CreateMatch(host);
        fixture.MatchMembership.Join(guest, match, "");
        match.AddReferee(guest.Id);
        var command = new MpRmRefCommand(fixture.SessionRegistry);

        var response = await command.HandleAsync(new MpCommandContext(host, ["guest"], match));

        Assert.Equal("guest removed from match referees.", response);
        Assert.False(match.IsReferee(guest.Id));
    }
}
