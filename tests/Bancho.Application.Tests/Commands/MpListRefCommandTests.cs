using Bancho.Application.Commands;
using static Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Bancho.Application.Tests.Commands;

/// <summary>Ported from app/commands.py's mp_listref.</summary>
public class MpListRefCommandTests
{
    [Fact]
    public async Task HandleAsync_ListsHostAndAddedReferees()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        fixture.RegisterAll(host, guest);
        var match = fixture.CreateMatch(host);
        fixture.MatchMembership.Join(guest, match, "");
        match.AddReferee(guest.Id);
        var command = new MpListRefCommand(fixture.SessionRegistry);

        var response = await command.HandleAsync(new MpCommandContext(host, [], match));

        Assert.Equal("host, guest.", response);
    }
}
