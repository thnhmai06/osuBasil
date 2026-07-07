using Bancho.Application.Commands;
using static Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Bancho.Application.Tests.Commands;

/// <summary>Ported from app/commands.py's mp_randpw.</summary>
public class MpRandpwCommandTests
{
    [Fact]
    public async Task HandleAsync_RandomizesPasswordTo16LowercaseHexChars()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var oldPassword = match.Password;
        var command = new MpRandpwCommand();

        var response = await command.HandleAsync(new MpCommandContext(host, [], match));

        Assert.Equal("Match password randomized.", response);
        Assert.NotEqual(oldPassword, match.Password);
        Assert.Equal(16, match.Password.Length);
        Assert.Matches("^[0-9a-f]{16}$", match.Password);
    }
}
