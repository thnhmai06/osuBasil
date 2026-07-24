using Basil.Application.PacketHandlers.Multiplayer;
using static Basil.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Basil.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's MatchChangePassword.</summary>
public class MatchChangePasswordHandlerTests
{
    [Fact]
    public async Task Handle_NonHost_NoOp()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        fixture.RegisterAll(host, guest);
        var match = fixture.CreateMatch(host);
        await fixture.MatchMembership.Join(guest, match, "");
        var handler = new MatchChangePasswordHandler(fixture.MatchMembership);

        await handler.HandleAsync(guest,
            MatchRequestReader(0, match.Name, "newpw", "Some Map", 100, new string('a', 32), guest.Id));

        Assert.Equal("", match.Password);
    }

    [Fact]
    public async Task Handle_Host_ChangesPassword()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var handler = new MatchChangePasswordHandler(fixture.MatchMembership);

        await handler.HandleAsync(host,
            MatchRequestReader(0, match.Name, "newpw", "Some Map", 100, new string('a', 32), host.Id));

        Assert.Equal("newpw", match.Password);
    }
}